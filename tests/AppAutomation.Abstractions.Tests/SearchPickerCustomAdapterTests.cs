using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SearchPickerCustomAdapterTests
{
    [Test]
    public async Task ModelBackedAdapter_SelectsObjectWithoutCreatingPopupControls()
    {
        var model = new LocationSelectionModel(
        [
            new LocationOption("Pickup option"),
            new LocationOption("Search result")
        ]);
        var page = new LocationPage(
            new EmptyResolver().WithAdapters(new LocationPickerAdapter(model)));

        page.SearchAndSelect(
            static candidate => candidate.LocationPicker,
            "pickup",
            "Pickup option");

        using (Assert.Multiple())
        {
            await Assert.That(model.SearchText).IsEqualTo("pickup");
            await Assert.That(model.SelectedOption).IsSameReferenceAs(model.Options[0]);
            await Assert.That(model.CanSave).IsTrue();
            await Assert.That(page.LocationPicker.SelectedItemText).IsEqualTo("Pickup option");
        }
    }

    private sealed record LocationOption(string Name);

    private sealed class LocationSelectionModel
    {
        public LocationSelectionModel(IReadOnlyList<LocationOption> options)
        {
            Options = options;
        }

        public IReadOnlyList<LocationOption> Options { get; }

        public string SearchText { get; set; } = string.Empty;

        public LocationOption? SelectedOption { get; set; }

        public bool CanSave => SelectedOption is not null;
    }

    private sealed class LocationPickerAdapter : IUiControlAdapter
    {
        private readonly ISearchPickerControl _control;

        public LocationPickerAdapter(LocationSelectionModel model)
        {
            _control = new ModelBackedSearchPickerControl(model);
        }

        public bool CanResolve(Type requestedType, UiControlDefinition definition)
        {
            return requestedType == typeof(ISearchPickerControl)
                && string.Equals(definition.PropertyName, "LocationPicker", StringComparison.Ordinal);
        }

        public object Resolve(
            Type requestedType,
            UiControlDefinition definition,
            IUiControlResolver innerResolver)
        {
            return _control;
        }
    }

    private sealed class ModelBackedSearchPickerControl : ISearchPickerControl
    {
        private readonly LocationSelectionModel _model;

        public ModelBackedSearchPickerControl(LocationSelectionModel model)
        {
            _model = model;
        }

        public string AutomationId => "LocationPicker";

        public string Name => "Location picker";

        public bool IsEnabled => true;

        public string SearchText => _model.SearchText;

        public string? SelectedItemText => _model.SelectedOption?.Name;

        public IReadOnlyList<string> Items => _model.Options.Select(static option => option.Name).ToArray();

        public void Search(string value)
        {
            _model.SearchText = value;
        }

        public void Expand()
        {
        }

        public void Select(string itemText) => SelectItem(itemText);

        public void SelectItem(string itemText)
        {
            var selected = _model.Options.SingleOrDefault(option =>
                string.Equals(option.Name, itemText, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Location option '{itemText}' was not found.");
            _model.SelectedOption = selected;
        }
    }

    private sealed class LocationPage : UiPage
    {
        private static UiControlDefinition LocationPickerDefinition { get; } = new(
            "LocationPicker",
            UiControlType.SearchPicker,
            "LocationPicker",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public LocationPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl LocationPicker => Resolve<ISearchPickerControl>(LocationPickerDefinition);
    }

    private sealed class EmptyResolver : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("model-backed-headless");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            throw new InvalidOperationException(
                $"Control '{definition.PropertyName}' must be resolved by its registered adapter.");
        }
    }
}
