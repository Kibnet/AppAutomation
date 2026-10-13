using System.Runtime.InteropServices;

namespace AppAutomation.Abstractions;

internal static class TransientUiaLookupFailure
{
    private const int UnspecifiedComFailure = unchecked((int)0x80004005);

    internal static bool IsRetryable(Exception exception) =>
        exception is COMException comException && comException.HResult == UnspecifiedComFailure;
}
