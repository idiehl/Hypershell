namespace Hypershell.Services;

/// <summary>The same real component tree is used in a Work and the isolated guided preview.</summary>
public static class HyperlibHeaderRenderingSource
{
    public const string ComponentSource = """
        @using System.Text.Json
        @using HyperLib.Core
        @using HyperLib.Runtime
        @using Hypershell.Models
        @using Hypershell.Services
        @using Microsoft.AspNetCore.Components
        @using Microsoft.AspNetCore.Components.Rendering
        @using Microsoft.AspNetCore.Components.Web
        @inject RendererOptions Options
        @RenderHeader()
        @code {
            [Parameter, EditorRequired] public BlockContext Context { get; set; } = new();
            private HyperlibHeaderBlockDefinition? _row;

            protected override void OnParametersSet()
            {
                var json = Context.Text("layout");
                _row = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(json, ContentJson.Options);
                if (_row is not null)
                {
                    var layout = new HyperlibHeaderLayoutDefinition { Enabled = true, Blocks = [_row] };
                    HyperlibHeaderLayouts.Validate(layout);
                    foreach (var element in HyperlibHeaderLayouts.Elements(layout).Where(item => item.Kind == "template"))
                        foreach (var block in element.TemplateBlocks)
                        {
                            TemplateValidator.ValidateId(block.Id, "header template block");
                            var schema = Options.Template.Blocks.SingleOrDefault(item => item.Id == block.Type)
                                ?? throw new InvalidDataException("This header template object is not in the project's owned library.");
                            TemplateValidator.ValidateValues(schema.Fields, Options.WithFieldDefaults(block).Values, "header template object");
                        }
                }
            }

            private RenderFragment RenderHeader() => builder =>
            {
                if (_row is not { } row) return;
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "studio-header-row");
                builder.AddAttribute(2, "data-header-row", row.Id);
                builder.AddAttribute(3, "style", HyperlibHeaderLayouts.RowStyle(row));
                builder.AddAttribute(4, "data-studio-region", Context.Text("studioRegion") is "navigation" or "body" or "footer" ? Context.Text("studioRegion") : "header");
                builder.OpenElement(5, "div");
                builder.AddAttribute(6, "class", "studio-header-row-content " + HyperlibHeaderLayouts.ContentClass(row.Arrangement));
                builder.AddAttribute(7, "data-header-arrangement", row.Arrangement);
                builder.AddAttribute(8, "style", HyperlibHeaderLayouts.ContainerStyle(row.Arrangement));
                foreach (var element in row.Elements) RenderElement(builder, element, row.Arrangement);
                builder.CloseElement();
                builder.CloseElement();
            };

            // Keep keyed elements as siblings in the same render-tree scope so reordering
            // preserves their interactive component instances and current tool state.
            private void RenderElement(RenderTreeBuilder builder, HyperlibHeaderElementDefinition element, string arrangement)
            {
                builder.OpenElement(0, "div");
                builder.SetKey(element.Id);
                builder.AddAttribute(1, "class", "studio-header-element studio-header-" + element.Kind);
                builder.AddAttribute(2, "data-header-element", element.Id);
                builder.AddAttribute(3, "style", HyperlibHeaderLayouts.ElementStyle(element, arrangement));
                builder.AddAttribute(4, "data-header-x", HyperlibHeaderLayouts.Coordinate(element.X));
                builder.AddAttribute(5, "data-header-y", HyperlibHeaderLayouts.Coordinate(element.Y));
                builder.AddAttribute(6, "data-header-width-mode", element.WidthMode);
                builder.AddAttribute(7, "data-header-width-percent", HyperlibHeaderLayouts.Coordinate(element.WidthPercent));
                builder.AddAttribute(8, "data-header-width-pixels", HyperlibHeaderLayouts.Coordinate(element.WidthPixels));
                builder.AddAttribute(9, "data-header-height", HyperlibHeaderLayouts.Coordinate(element.Height));
                builder.AddAttribute(10, "data-header-alignment", element.PositionAlignment);
                if (element.IsContentSlot) { builder.AddAttribute(11, "data-content-slot", "true"); builder.AddAttribute(12, "data-slot-name", element.SlotName); }
                var emptySlot = element.IsContentSlot && (element.Kind == "text" ? string.IsNullOrWhiteSpace(element.Text)
                    : element.Kind is "image" or "video" && string.IsNullOrWhiteSpace(element.MediaPath));
                if (emptySlot)
                {
                    if (Context.Editing || Options.GenerationId == "preview")
                    {
                        builder.OpenElement(13, "div"); builder.AddAttribute(14, "data-studio-slot-placeholder", "true");
                        builder.AddAttribute(15, "style", "box-sizing:border-box;min-height:36px;height:100%;padding:8px;outline:1px dashed currentColor;outline-offset:-2px;display:flex;flex-direction:column;justify-content:center;font:12px system-ui;overflow:hidden");
                        builder.OpenElement(16, "strong"); builder.AddContent(17, string.IsNullOrWhiteSpace(element.SlotName) ? element.Kind == "text" ? "Text content" : element.Kind == "video" ? "Video content" : "Image content" : element.SlotName); builder.CloseElement();
                        builder.OpenElement(18, "span"); builder.AddContent(19, string.IsNullOrWhiteSpace(element.SlotHint) ? "Add content in Authoring." : element.SlotHint); builder.CloseElement();
                        builder.CloseElement();
                    }
                }
                else if (element.Kind == "text")
                {
                    builder.OpenElement(20, "div");
                    builder.AddAttribute(21, "class", "studio-header-text");
                    builder.AddContent(22, element.Text);
                    builder.CloseElement();
                }
                else if (element.Kind == "box")
                {
                    builder.OpenElement(30, "div");
                    builder.AddAttribute(31, "class", "studio-header-box-content " + HyperlibHeaderLayouts.ContentClass(element.Arrangement));
                    builder.AddAttribute(32, "data-header-arrangement", element.Arrangement);
                    builder.AddAttribute(33, "style", HyperlibHeaderLayouts.ContainerStyle(element.Arrangement));
                    foreach (var child in element.Children) RenderElement(builder, child, element.Arrangement);
                    builder.CloseElement();
                }
                else if (element.Kind == "template")
                {
                    builder.OpenElement(40, "div");
                    builder.AddAttribute(41, "class", "studio-header-template-content");
                    builder.AddAttribute(42, "data-studio-shared-template", "true");
                    builder.AddAttribute(43, "data-header-template", element.TemplateObjectId);
                    builder.AddAttribute(44, "style", HyperlibHeaderLayouts.TemplateStyle(element.TemplateAppearance));
                    foreach (var block in element.TemplateBlocks) builder.AddContent(45, RenderTemplateBlock(element, block));
                    builder.CloseElement();
                }
                else if (element.Kind == "navigation")
                {
                    builder.OpenComponent<DynamicComponent>(50);
                    builder.AddAttribute(51, "Type", Options.Resolve("studio-navigation-links"));
                    builder.AddAttribute(52, "Parameters", new Dictionary<string, object> { ["Context"] = new BlockContext {
                        Id = Context.Id + "--" + element.Id, Editing = false,
                        Values = new() { ["settings"] = ContentJson.Value(JsonSerializer.Serialize(element.Navigation, ContentJson.Options)) } } });
                    builder.CloseComponent();
                }
                else if (string.IsNullOrEmpty(element.MediaPath))
                {
                    var label = element.Kind == "video" ? "Video" : "Image";
                    builder.OpenElement(60, "div");
                    builder.AddAttribute(61, "class", "studio-header-media-placeholder");
                    builder.AddAttribute(62, "role", "img");
                    builder.AddAttribute(63, "aria-label", string.IsNullOrWhiteSpace(element.Alt) ? label : element.Alt);
                    builder.AddContent(64, label);
                    builder.CloseElement();
                }
                else
                {
                    builder.OpenElement(70, element.Kind == "video" ? "video" : "img");
                    builder.AddAttribute(71, "src", element.MediaPath);
                    builder.AddAttribute(72, element.Kind == "video" ? "aria-label" : "alt", element.Alt);
                    builder.AddAttribute(73, "style", HyperlibHeaderLayouts.MediaStyle(element));
                    if (element.Kind == "video")
                    {
                        builder.AddAttribute(74, "controls", true);
                        builder.AddAttribute(75, "playsinline", true);
                        builder.AddAttribute(76, "preload", "metadata");
                    }
                    builder.CloseElement();
                }
                builder.CloseElement();
            }

            private RenderFragment RenderTemplateBlock(HyperlibHeaderElementDefinition element, PageBlock block) => builder =>
            {
                var effective = Options.WithFieldDefaults(block);
                var instanceId = Context.Id + "--" + element.Id + "--" + block.Id;
                builder.OpenElement(0, "section");
                builder.SetKey(instanceId);
                builder.AddAttribute(1, "class", "hl-block studio-header-template-block " + Options.BlockTypeClass(effective) + " " + RendererOptions.BlockSpanClass(effective));
                builder.AddAttribute(2, "data-block-id", instanceId);
                builder.AddAttribute(3, "data-block-type", block.Type);
                builder.AddAttribute(4, "style", RendererOptions.BlockStyle(effective));
                builder.OpenComponent<ErrorBoundary>(5);
                builder.SetKey(instanceId + ":" + JsonSerializer.Serialize(effective.Values, ContentJson.Options));
                builder.AddAttribute(6, "ChildContent", (RenderFragment)(child =>
                {
                    child.OpenComponent<DynamicComponent>(0);
                    child.AddAttribute(1, "Type", Options.Resolve(block.Type));
                    child.AddAttribute(2, "Parameters", new Dictionary<string, object> { ["Context"] = new BlockContext { Id = instanceId, Editing = false, Values = effective.Values } });
                    child.CloseComponent();
                }));
                builder.AddAttribute(7, "ErrorContent", (RenderFragment<Exception>)(error => child =>
                {
                    child.OpenElement(0, "p"); child.AddAttribute(1, "role", "alert");
                    child.AddContent(2, "This template object could not render: " + error.Message); child.CloseElement();
                }));
                builder.CloseComponent();
                builder.CloseElement();
            };
        }
        """;
}
