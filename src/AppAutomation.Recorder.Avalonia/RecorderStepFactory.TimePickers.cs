using AppAutomation.Abstractions;
using Avalonia.Controls;
using static AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    internal bool TryResolvePresenterTimeBinding(Control source, out RecorderTimePickerBinding binding, out string error) =>
        RecorderTimePickerBinding.TryResolve(_options, _validationRootProvider?.Invoke(), source, out binding, out error);

    internal bool IsPresenterTimeSource(Control source) =>
        _options.TimePickerHints.Any(hint => RecorderTimePickerBinding.IsCandidate(source, hint));

    internal StepCreationResult TryCreatePresenterTimeStep(RecorderTimePickerBinding binding, TimeSpan? value)
    {
        try
        {
            if (value is null)
                return StepCreationResult.Unsupported("A committed null time can be read, but SetTime cannot replay a null action.");
            binding.Owner.ValidateValue(value.Value, binding.Hint.Parts);
            return CreateStep(binding.Root, new RecordedStep(RecordedActionKind.SetTime,
                PresenterTimeDescriptor(binding), TimeValue: value), "Recorded confirmed presenter time from its committed UI property.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return StepCreationResult.Unsupported("The committed time cannot be replayed without loss: " + exception.Message);
        }
    }

    private SemanticValueCandidate PresenterTimeCandidate(RecorderTimePickerBinding binding) =>
        new(PresenterTimeDescriptor(binding), RecorderValueKind.Time, RecorderValueAccessorKind.SelectedTime,
            TimeValue: binding.ReadValue());

    private RecordedControlDescriptor PresenterTimeDescriptor(RecorderTimePickerBinding binding) =>
        CreateCompositeDescriptor(binding.Hint.LocatorValue, UiControlType.TimePicker, binding.Hint.LocatorKind,
            binding.Hint.FallbackToName, binding.Root, warning: null);
}
