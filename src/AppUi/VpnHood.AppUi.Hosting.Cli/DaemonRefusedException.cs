namespace VpnHood.AppUi.Hosting.Cli;

// The service answered, and the answer was no: whoever asked may not use the app on this computer.
// The message is the service's own sentence, which a command prints as it is.
public class DaemonRefusedException(string message) : Exception(message);
