# v1.1.177
* Fix: Client close the entire VPN connection when a requested site refuse a connection

# v1.1.176
* Feature: Client can detect its expired session

### Client
* Change: Always Open the main window at start if App is already running

### Developer
* Change: Update TcpDatagramHeader from binary to TcpDatagramChannelRequest json
* Change: Move IDevice and IPacketCapture to VpnHood.Client.Device module
* Developer: Add Simple Sample for Windows Client usage
* Developer: Fix PublishApps.ps1 scripts to create publish folder when it does not exist

# v1.1.138
* Fix: Checking update from the internet

### Server
* Update: add subdomain when creating self-signed certifiates with random CN

# v1.1.91
* Fix: AppUpdater throw error if UpdateUrl in publish.json was empty string

### Client
* Update: Add client prefix to Bug Report File Name
* Update: Close Bug Report bottom page after sending report
* Update: Separate SPA from VpnHood.Client.App.UI. Make it easier for developers to use custom SPA
* Update: Change Anonymous IP masking from *.*.x.x to "*.x.x.*"
* Update: Diagnose set Last error to "Diagnose has been finished" if there is not other error
* Fix: Dark Icon
* Fix: Open BugReport page on external web browser
* Fix: Disable Diagnose button when a connection already diagnosing
* Fix: Reporting .NET version instead of App Version

# v1.1.75
* Initial Release








