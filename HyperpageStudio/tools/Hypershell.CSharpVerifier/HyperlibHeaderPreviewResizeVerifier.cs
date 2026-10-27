public static class HyperlibHeaderPreviewResizeVerifier
{
    public static Task<string> RunAsync(string outputDirectory,string libraryRoot)
        =>HyperlibHeaderPreviewDragVerifier.RunAsync(outputDirectory,libraryRoot,"verify-header-preview-resize.cjs");
}
