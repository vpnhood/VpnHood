using System.IO.Pipes;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// How a launch that finds the window open lets it come forward, where only the launch the person just
// started may bring a window forward: it hands that right to the process at the pipe's far end.
public interface IForegroundHandOff
{
    void HandTo(NamedPipeClientStream window);
}
