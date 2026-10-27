using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace Meridian.TaskLauncher;

internal static class KeySetup
{
    internal static int ShowDialog()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "Meridian Secure MCP Tunnel setup", Width = 560, Height = 270,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false };
        var text = new Label { Left=22, Top=20, Width=500, Height=55,
            Text="Paste the restricted OpenAI runtime API key below.\nThe key is hidden and saved encrypted for this Windows user." };
        var input = new TextBox { Left=22, Top=85, Width=500, UseSystemPasswordChar=true };
        var status = new Label { Left=22, Top=125, Width=500, Height=42,
            Text="Paste the full OpenAI secret key, not the Tunnel ID, name, or tracking ID." };
        var save = new Button { Left=340, Top=180, Width=90, Text="Save key" };
        var cancel = new Button { Left=440, Top=180, Width=82, Text="Cancel", DialogResult=DialogResult.Cancel };
        var saved = false;
        save.Click += (_,_) =>
        {
            try
            {
                var key = input.Text.Trim();
                if (!IsRuntimeKey(key))
                { status.Text="Enter the complete restricted API key beginning with sk-."; return; }
                Store(key);
                input.Clear(); input.Enabled=false; saved=true; save.Enabled=false;
                status.Text="Key stored encrypted. You can close this window and return to Codex.";
                cancel.Text="Close";
            }
            catch { status.Text="The key could not be stored safely. No key was logged; ask Codex to check the private setup folder."; }
        };
        form.Controls.AddRange(new Control[]{ text,input,status,save,cancel });
        form.AcceptButton=save; form.CancelButton=cancel;
        form.ShowDialog();
        input.Clear();
        return saved ? 0 : 1;
    }

    internal static string ValidateStateRoot()
    {
        var root = TaskLaunch.RegularPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasMeridian", "McpCompanion", "SecureTunnel"));
        var marker = TaskLaunch.RegularPath(Path.Combine(root,".meridian-secure-tunnel-state"));
        if (!Directory.Exists(root) || !File.Exists(marker) || File.ReadAllText(marker).Trim() != "atlas-meridian-secure-tunnel-v1")
            throw new IOException("The private Secure Tunnel state has not been initialized.");
        var user=WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException();
        var acl=new DirectoryInfo(root).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var rules=acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (!acl.AreAccessRulesProtected || !Equals(acl.GetOwner(typeof(SecurityIdentifier)),user) || rules.Length != 2)
            throw new IOException("The private state owner or permissions changed.");
        foreach (var rule in rules)
        {
            if ((rule.IdentityReference.Value != user.Value && rule.IdentityReference.Value != "S-1-5-18")
                || rule.AccessControlType != AccessControlType.Allow || rule.FileSystemRights != FileSystemRights.FullControl
                || rule.IsInherited || rule.InheritanceFlags != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
                || rule.PropagationFlags != PropagationFlags.None)
                throw new IOException("The private state permissions changed.");
        }
        return root;
    }

    internal static void Store(string key)
    {
        if (!IsRuntimeKey(key)) throw new ArgumentException("A complete OpenAI secret key is required.");
        var root = ValidateStateRoot();
        var path = TaskLaunch.RegularPath(Path.Combine(root,"runtime-key.dpapi"));
        var temporary = TaskLaunch.RegularPath(Path.Combine(root,"runtime-key.dpapi.new"));
        var protectedValue=Protect(key);
        // ConvertFrom-SecureString's Windows default is a CurrentUser DPAPI Unicode blob in hex.
        File.WriteAllText(temporary, protectedValue, new UTF8Encoding(false));
        File.Move(temporary,path,true);
    }

    internal static bool IsRuntimeKey(string key) => key.Length >= 20 && key.StartsWith("sk-", StringComparison.Ordinal)
        && !key.Any(c => char.IsWhiteSpace(c) || c is '\'' or '"');

    internal static string Protect(string key)
    {
        var bytes=Encoding.Unicode.GetBytes(key);
        var input=new DataBlob { Length=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        var output=new DataBlob();
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            if (!CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var encrypted=new byte[output.Length];
            Marshal.Copy(output.Data,encrypted,0,encrypted.Length);
            return Convert.ToHexString(encrypted).ToLowerInvariant();
        }
        finally
        {
            Array.Clear(bytes);
            for(var i=0;i<input.Length;i++) Marshal.WriteByte(input.Data,i,0);
            Marshal.FreeHGlobal(input.Data);
            if(output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
