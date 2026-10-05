using Kiosk.Server.Components.Shared;
using KioskClinicaPC.Core.Config;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kiosk.Server.Tests;

// Exercise the actual Razor change handler, including Blazor's normalization of datetime-local.
#pragma warning disable BL0006
public sealed class EventScheduleEditorTests
{
    [Theory]
    [InlineData("event-start", "2026-12-01T10:30:00")]
    [InlineData("event-end", "2026-12-08T18:45:00")]
    public async Task Date_changes_with_normalized_seconds_update_draft_and_notify_parent(string input, string value)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new DateInputRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var ev = new KioskEvent { Start = new(2026, 10, 5), End = new(2026, 10, 12) };
        int changed = 0;
        await renderer.Mount(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(EventScheduleEditor.Event)] = ev,
            [nameof(EventScheduleEditor.StoreTimeZone)] = TimeZoneInfo.Utc,
            [nameof(EventScheduleEditor.OnChanged)] = EventCallback.Factory.Create(this, () => changed++)
        }));
        await renderer.Change(input, value);
        Assert.Equal(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), input == "event-start" ? ev.Start : ev.End);
        Assert.Equal(1, changed);
    }

    private sealed class DateInputRenderer(IServiceProvider services, ILoggerFactory logger) : Renderer(services, logger)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        private int _component;
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Date input render failed", exception);
        public Task Mount(ParameterView parameters) => Dispatcher.InvokeAsync(async () =>
        {
            _component = AssignRootComponentId(new EventScheduleEditor());
            await RenderRootComponentAsync(_component, parameters);
        });
        public Task Change(string inputId, string value) => Dispatcher.InvokeAsync(async () =>
        {
            var tree = GetCurrentRenderTreeFrames(_component);
            for (int index = 0; index < tree.Count; index++)
            {
                var frame = tree.Array[index];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "input") continue;
                string? id = null;
                ulong handler = 0;
                for (int attribute = index + 1; attribute < tree.Count && tree.Array[attribute].FrameType == RenderTreeFrameType.Attribute; attribute++)
                {
                    var item = tree.Array[attribute];
                    if (item.AttributeName == "id") id = item.AttributeValue?.ToString();
                    if (item.AttributeName == "onchange") handler = item.AttributeEventHandlerId;
                }
                if (id != inputId) continue;
                Assert.NotEqual(0UL, handler);
                await DispatchEventAsync(handler, null, new ChangeEventArgs { Value = value });
                return;
            }
            throw new InvalidOperationException("Date input missing: " + inputId);
        });
    }
}
#pragma warning restore BL0006
