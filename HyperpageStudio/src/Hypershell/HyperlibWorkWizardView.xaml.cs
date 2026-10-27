using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hypershell.ViewModels;

namespace Hypershell;

public partial class HyperlibWorkWizardView : UserControl
{
    private HyperlibWorkWizardViewModel? _wizard;
    private bool _compact;
    private (int Step, string TemplateId)? _choicesLocation;
    public HyperlibWorkWizardView()
    {
        InitializeComponent(); DataContextChanged+=(_,_)=>Connect(); Loaded+=(_,_)=>{Connect();ArrangeBuilder();};
        PageBuilderPreview.HeaderDragRequested+=HeaderDragRequested;
        PageBuilderPreview.HeaderResizeRequested+=HeaderResizeRequested;
        SizeChanged+=(_,_)=>ArrangeBuilder();
        Unloaded+=(_,_)=>{if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=null;};
    }
    private void Connect()
    {
        if(!ReferenceEquals(_wizard,DataContext))_choicesLocation=null;
        if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=DataContext as HyperlibWorkWizardViewModel;
        if(_wizard is not null)_wizard.PropertyChanged+=Changed;ResetChoicesForStage();RefreshPreview();
    }
    private void Changed(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName==nameof(HyperlibWorkWizardViewModel.PreviewDefinition))RefreshPreview();
        if(args.PropertyName==nameof(HyperlibWorkWizardViewModel.CurrentStage))ResetChoicesForStage();
    }
    private void ResetChoicesForStage()
    {
        if(_wizard is null)return;
        var location=(_wizard.Step,_wizard.CurrentTemplate.Id);
        if(_choicesLocation==location)return;
        _choicesLocation=location;ChoicesScroll.ScrollToTop();
    }
    private void StageObjectChoice_RequestBringIntoView(object sender,RequestBringIntoViewEventArgs args)
    {
        // Card focus and selection must not move the list or its containing panel.
        // Handle the request before it reaches either ScrollViewer; wheel and bar input still scroll normally.
        args.Handled=true;
    }
    private void RefreshPreview()
    {
        if(_wizard is null)return;
        var root=_wizard.TemplateLibraryRoot??(_wizard.OwnedProjectRoot is {} owned?System.IO.Path.GetFullPath(System.IO.Path.Combine(owned,"../..")):null);
        if(root is null)return;
        PageBuilderPreview.SetDraft(root,_wizard.PreviewDefinition,_wizard.CurrentTemplate.BuildDefinition(),_wizard.CurrentPageTitle,_wizard.OwnedProjectRoot,
            _wizard.IsHeaderStage||_wizard.IsNavigationStage||_wizard.IsBodyStage||_wizard.IsFooterStage?_wizard.CurrentTemplate.Id:null,
            _wizard.IsNavigationStage?"navigation":_wizard.IsBodyStage?"body":_wizard.IsFooterStage?"footer":"header");
    }
    private void HeaderDragRequested(object? sender,HyperlibHeaderDragEventArgs args)
    {
        if(_wizard is null||!IsLoaded||args.TemplateId!=_wizard.CurrentTemplate.Id)return;
        var layout=args.Region=="navigation"&&_wizard.IsNavigationStage?_wizard.CurrentTemplate.NavigationLayout
            :args.Region=="header"&&_wizard.IsHeaderStage?_wizard.CurrentTemplate.HeaderLayout
            :args.Region=="body"&&_wizard.IsBodyStage?_wizard.CurrentTemplate.BodyLayout
            :args.Region=="footer"&&_wizard.IsFooterStage?_wizard.CurrentTemplate.FooterLayout:null;
        if(layout is not null)args.Accepted=layout.TryMoveElement(args.ElementId,args.ParentId,args.X,args.Y,args.PreviousX,args.PreviousY);
    }
    private void HeaderResizeRequested(object? sender,HyperlibHeaderResizeEventArgs args)
    {
        if(_wizard is null||!IsLoaded||args.TemplateId!=_wizard.CurrentTemplate.Id)return;
        var layout=args.Region=="navigation"&&_wizard.IsNavigationStage?_wizard.CurrentTemplate.NavigationLayout
            :args.Region=="header"&&_wizard.IsHeaderStage?_wizard.CurrentTemplate.HeaderLayout
            :args.Region=="body"&&_wizard.IsBodyStage?_wizard.CurrentTemplate.BodyLayout
            :args.Region=="footer"&&_wizard.IsFooterStage?_wizard.CurrentTemplate.FooterLayout:null;
        if(layout is not null)args.Accepted=layout.TryResizeElement(args.ElementId,args.ParentId,args.Arrangement,args.Handle,
            args.X,args.Y,args.Width,args.Height,args.ParentWidth,args.ParentHeight,args.Previous);
    }
    private void ArrangeBuilder()
    {
        var compact=ActualWidth<780;if(compact==_compact&&ChoicesColumn.Width.Value>0)return;_compact=compact;
        if(compact)
        {
            ChoicesColumn.Width=new(1,GridUnitType.Star);PreviewColumn.Width=new(0);
            BuilderTopRow.Height=new(Math.Min(260,Math.Max(190,ActualHeight*.38)));BuilderBottomRow.Height=new(1,GridUnitType.Star);
            Grid.SetColumn(PreviewPanel,0);Grid.SetRow(PreviewPanel,0);Grid.SetRow(ChoicesScroll,1);ChoicesScroll.Padding=new(0,12,0,0);
        }
        else
        {
            ChoicesColumn.Width=new(5,GridUnitType.Star);PreviewColumn.Width=new(6,GridUnitType.Star);BuilderTopRow.Height=new(1,GridUnitType.Star);BuilderBottomRow.Height=new(0);
            Grid.SetColumn(PreviewPanel,1);Grid.SetRow(PreviewPanel,0);Grid.SetRow(ChoicesScroll,0);ChoicesScroll.Padding=new(0,0,14,0);
        }
    }
}
