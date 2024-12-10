# v5.0.608
### Client
* Feature: Improve premium sessions
* Feature: Update server token from multiple URLs
* Feature: Handle Trial Session
* Feature: Drop QUIC in protocol
* Feature: Protocol Version 6
* Feature: Add Portuguese (Brazil) language
* Improve: Improve initialization speed to exclude my country
* Improve: Diagnostic wrongly reports your internet may not be stable
* Improve: Check for the latest version after the connection is established
* Update: Translate many error messages
* Update: Add token tags
* Update: Add server tags
* Update: Generate anonymous client ID from device ID
* Update: Detect client country to exclude my country
* Fix: Startup crash on some Android devices
* Fix: IPv6 Ping

### Server
* Fix: Server listening to a test IP
* Fix: IpV6 Subnet in auto IP configuration
* Fix: IPv6 Ping
* Feature: Allow to config swap file size
* Feature: Allow to config UdpClient buffer size
* Feature: Protocol Version 6
* Feature: Allow to config TcpCongestionControl on Linux
* Update: File server-internal token format
* Improve: Bulk sync to AccessManager

### Development
* Update: Migrate to .NET 9.0
* Update: Use composite API for SPA


# v4.6.557
### Client
* Fix: ADs providers
* Feature: Implement InMobi ads

### Server
* Fix: Wrong IP in Default FileAccessServer Endpoints
* Fix: Could not use UDP when listening to all IPs

# v4.6.553
### Client
* Fix: Could not open websites on some devices
* Fix: "Connection is already in progress" error when the connection is already established
* Fix: Android: IPv6 over IPv4

### Server
* Feature: Linux: Add or remove listener IPs on the server at runtime via "AddListenerIpsToNetwork"
* Fix: DNS list

# v4.6.546
### Server
* Fix: AddressFamilyNotSupported error when server does not have IPv6

# v4.6.544
### Client
* Fix: App Filter does not work when no app is selected
* Fix: Exclude the ad tracker from the tunnel
* Fix: Exclude My Country
* Fix: Could not set MTU error
* Fix: Android: InApp Update 
* Fix: Android: VPN Service remains in memory after disconnect
* Feature: Report unreachable servers
* Feature: Add domain filtering to engine
* Feature: Add log to Android logcat
* Feature: Try to find a reachable server among endpoints
* Feature: Implement ChartBoost ads
* Update: Improve performance and memory usage
 
### Server
* Feature: Support multiple redirect endpoints
* Update: Use Cloudflare for detecting the server's public IP
* Update: Improve performance and memory usage

# v4.5.535
### Client
* Improve: App Filter page
* Improve: Android TV
* Update: Use Interstitial Ad instead of rewarded
* Update: Show client country when Exclude My Country is enabled
* Feature: Support multiple ad networks
* Feature: Add Turkey, German, and Hindi languages
* Fix: Some error message after disconnection
* Fix: Disconnect button doesn't work while connecting

# v4.5.522
### Client
* Update: Disable "Exclude My Country" when unable to load the IP location database
* Update: Automatically disconnect client after changing exclude my country settings
* Improve: Performance & Speed on the connection when the user has selected exclude my country
* Fix: Freeze network on some situation

### Server
* Update: Improve performance

# v4.5.520
### Client
* Feature: Allow to select servers by country if the server supports it
* Feature: Auto pause and resume when the server is not reachable
* Update: User IncludeIpRanges and ExcludeIpRanges in settings instead of CustomIpRanges
* Update: Try to fix accesskey when it is not valid by missing the padding characters
* Update: Countries IP ranges
* Fix: Some wrong message in disconnect

### Server
* Improve: Optimize server reconfiguration at runtime
* Feature: Support selecting server location when controlled by HttpAccessManager
* Feature: Enable hot restart for FileAccessManager
* Deprecate: ServerProtocol Version 2 (451) is deprecated and no longer supported

# v4.4.506
### Client
* Update: Android: Show Notification & QuickLaunch Request after connect
* Update: Display AD without VPN
* Fix: Android: Crash when starting as always-on, but there is no selected server
* Fix: Android: importing access key by file
* Fix: minor issue in disconnect messages
* Deprecate: ServerProtocol Version 2 (451) is deprecated and no longer supported

### Server
* Update: Support Flexible Ad

# v4.4.500 
### Client
* Update: Retry failed connection if the access token is updated from the server token URL
* Update: Move VpnHood Public Servers to VpnHood Connect App
* Fix: Ad service
* Fix: Sometimes the connect button didn't disconnect the current connection
* Fix: Android: Crash on disconnect
* Fix: Android: Diagnoser mistakenly shows "Connection is not stable"
* Fix: Android: Google Play Update
* Update: Android: Improve Android TV

### Server
* Feature: Ad reward service

# v4.3.485
### Client
* Feature: Accept DNS Servers from the connected Server
* Feature: Multi-language Support (Arabic, Chinese, English, Persian, Portuguese, Russian, Spanish)
* Feature: Support Billing integration
* Feature: Implement Google Billing
* Feature: Support Interface for Account integration
* Feature: Implement VpnHood Store Account
* Update: Improve UDP performance
* Update: Prepare to migrate VpnHood public servers to VpnHood Connect

### Server
* Feature: Configure DNS Servers for the connected clients
* Update: Support multiple certificates
* Update: Configure timeout for HTTP-01 DNS challenge 
* Update: Faster shutdown

# v3.3.475
### Client
* Feature: Update UI when the server does not support UDP tunnel
* Fix: Crash when pressing "Disconnect" while connecting
* Fix: Remove the "No data has been received" message before the connection is established
* Update: Protocol Changed: Server sends UdpPort instead of UdpEndPoints

### Server
* Fix: Crash when pressing "Disconnect" while connecting
* Update: Protocol Changed: Server sends UdpPort instead of UdpEndPoints
* Feature: Support HTTP-01 DNS challenge to auto-verify domains

# v3.3.463
### Client
* Fix: The "Connect" button for free public servers does not work under certain circumstances

# v3.3.462
### Client
* Fix: Pull the recent tokens from the server & URL

### Server
* Fix: Push the recent tokens to clients

# v3.3.454
### Client
* Update: New Access Token format
* Update: Use OS encryption for TLS
* Feature: Enable updating server endpoints by Url
* Feature: Display token expiration if it exists
* Feature: Update server token URL from the server
* Feature: Android: Prompt for the update by Google Play if possible
* Feature: Android: Support opening (KEY) as VpnHood Key file
* Fix: Windows: Freeze network after auto-reconnect redirects to a new server
* Deprecate: Server ProtocolVersion 3 is deprecated and no longer supported

### Server
* Feature: Generate encrypted server token to client update server endpoint from a URL
* Update: Move some File Access Manager options from the gen command to config
* Update: Return 401 instead of 400 for invalid request
* Update: Use OS encryption for TLS
* Update: Redact more data in server logs
* Deprecate: Server ProtocolVersion 3 is deprecated and no longer supported

# v3.2.439
### Client
* Update: Improve Performance
* Feature: Add the initializing state to the connection state
* Feature: Android Set the navigation button background color

### Server
* Update: Improve Performance
* Update: Improve error reporting to access manager
* Update: Improve reporting state to access manager
* Fix: Server doesn't start when there is no local IpV6
* Fix: Server freezes when default UDP port is not available
* Fix: Send status even if server configuration is not successful
* Security: Updating AccessManaget token

### Developer
* Upgrade to .NET 8
* Android: Migrate from Xamarin to .NET Android Application
* Android: Upgrade to Android API 34

# v3.1.436 
### Client
* Update: Improve UI
* Fix: Android: Keyboard cover input fields in the UI
* Fix: Android: Frequently asking to add the system tile

# v3.1.430 
### Client
* Update: Improve UI
* Update: Windows: Remove from the taskbar on minimize
* Update: Android: Ask for notification permission
* Update: Android: Add compatibility to Android 6 & 14
* Feature: Android: Register vh and vhkey intent for importing access key
* Feature: Android: Add quick settings tile
* Fix: Android: Requesting permission for notification
* Fix: Windows: Restore Windows state to normal when user clicks on the system tray icon
* Fix: Improve disconnecting speed

### Developement
* Client: Update SPA to VUE 3 and TypeScript
* Client: Create Api.ts generated for TypeScript by nswag

# v3.0.429
### Client
* Android: Fix not showing apps in AppFilter

# v3.0.428
### Server
* Fix: Certificate was not updated immediately by Access Manager
* Fix: AutoUpdater

# v3.0.427
### Client
* Feature: Android: Support opening (key) as VpnHood Key file
* Update: Windows: Fix Windows Firewall Configuration
* Update: Windows: Set fixed window size

# v3.0.423
### Client
* Feature: Android: Support opening Cinderella file (CDY) as VpnHood Key file
* Feature: Android: Changing file signature for no-google-store APK
* Update: Remove the legacy Protocol Version 3
* Update: Improve performance and battery usage
* Update: Android: Minimum requirement has been increased to Android 6.0 (Marshmallow)
 
### Server
* Update: Remove the legacy Protocol Version 3
* Fix: Collection was modified error, which caused connection freeze temporary
 
# v3.0.416
### Client
* Feature: Offer Premium if VpnHood Public server selected

# v3.0.412
### Client
* Fix: Invalid UDP packet signature
* Fix: Android: setMetered error

### Server
* Fix: Invalid UDP packet signature
* Update: Reduce server default logging

# v3.0.411
### Client
* Feature: Client Protocol Version 4 
* Feature: TCP connection reuse
* Feature: Allow Drop UDP packets
* Feature: Add IPv6 support to country exclusion/inclusion
* Update: Improve reliability
* Update: Improve logging by adding channel Id
* Update: Include internal exception messages and stack trace in the diagnostic file
* Update: Improve connection speed
* Fix: Some UDP Packet loss
* Fix: Android: VpnHood system notification
* Fix: Remain in Disconnecting state
* Fix: Windows: VpnHood Window display can't reach this page instead of UI
* Warning: Preparing to deprecate v2.8.361 (Protocol Version 3)

### Server
* Feature: Server Protocol Version 4 
* Feature: TCP connection reuse
* Update: Improve reliability
* Update: Improve logging by adding channel Id
* Update: Returns bad request for any unknown or unauthorized access
* Fix: Server Kernel SendBufferSize
* Fix: ClientCount report 0 in the FileAccessManager log
* Fix: Some UDP Packet loss
* Fix: Reporting CPU usage
* Warning: Preparing to deprecate v2.8.361 (Protocol Version 3)

### Development
* Update: Use IAsyncDisposable
* Update: Improve tests and make them faster
* Feature: Add graceful disconnection


# v2.9.370
### Server
* Fix: Restart listener on servers by UdpEndPoints changes

# v2.9.369
### Client & Server
* Feature: Use shared UDP port 
* Feature: Improve protocol anonymity and anti-fingerprinting for UDP 
* Feature: Add Server Secret in addition to session secret
* Update: Improve security, performance, and battery usage
* Update: Remove excessive error logs on disconnecting
* Update: 64-bit session Id
* Update: Client Protocol Version 3
* Update: Server Protocol Version 3

# v2.8.361
### Server
* Feature: Enable hot reconfigure for VpnHood Server TCP listener to avoid unnecessary restarts on unchanged endpoints
* Update: Replace AllowIpV6 to BlockIpV6
* Update: Improve server security 

# v2.8.360
### Client
* Fix: Used traffic was not displayed correctly

### Server
* Fix: Used traffic was not reported correctly

# v2.7.357
### Client
* Fix: Windows: Too long filter expression error

# v2.7.356
### Client
* Feature: Windows: Add "Open in Browser" item to system menu
* Update: windows: ""Open in browser" if WebView is not initialized properly

### Server
* Feature: Merge Server Configuration
* Fix: Setting TCP kernel buffer
* Fix: Error in parsing IPNetwork as Range
* Update: Move NetFilter event from log to track
* Update: Set Send Kernel TCP buffer sizes
* Update: Use 24h in filename in track archives

# v2.7.350
### Client
* Feature: Follow server-supported networks by IP range
* Update: Performance improvement
* Windows: Fix Auto Updater

### Server
* Feature: Filter server local networks
* Feature: Filter networks by IP ranges
* Fix: Missing some NetProtector log
* Fix: Windows: AutoUpdater
* Security: Always block access to loopback addresses
* Update: log file archive format

# v2.6.346
### Client
* Update: Improve stability when using no UDP mode

### Server
* Feature: Improve stability by adding lifetime to TcpDatagramChannel
* Fix: IpV6 detection

# v2.6.342
### Client
* Fix: UDP port memory leak
* Feature: Notify when a new version is available
* Update: Add the build version on the top right of the screen
* Update: Windows: Switch to MSI package to prevent False positive virus detection

### Server
* Fix: UDP port memory leak
* Update: Separate new/close session logs
* Update: Improve log format
* Update: Change log files extension from txt to log

# v2.6.339
### Server
* Feature: Report server public IPs to log
* Update: Improve IPv6 stability

### Server
* Fix: It doesn't generate log
* Feature: Add Linux-arm64 installation

# v2.6.336
### Client
* Update: Optimizing UDP Processing
* Update: Improving Garbage Collector
* Update: Async Disposal
* Update: Windows: Upgrade WinDiver to 2.2.2
* Update: Improve performance

### Server
* Feature: Allow disabling LogAnonymizer in the server config
* Feature: NetScanner protector
* Feature: Access ServerConfig overwrite
* Feature: UdpProxyPoolEx
* Update: Optimizing UDP Processing
* Update: Reporting improved; prevent too many duplicate errors
* Update: Windows updater write its log
* Update: Improve performance
* Update: Add NetScan to Track log
* Update: Improve the Tracer Log File format
* Fix: File Access Server throw access volatile randomly
* Fix: Disconnecting Idle users after an hour of inactivity of FileAccess Server
* Fix: Linux Auto Installation
* Fix: Too many session recoveries after hot restart

# v2.6.329
### Server
* Fix: Report CPU Usage on Linux
* Fix: Windows Server Auto Update
* Fix: Windows Auto Install
* Fix: Stop accepting connection on specific errors
* Update: Report more config on start up

# v2.6.327
### Server
* Fix: Error on centos
* Feature: Report CPU usage to access server
* Feature: Add TcpConnectWait control
* Feature: Add TcpChannelCount control

# v2.6.326
### Client
* Feature: Windows: Compile as Win-x64. NET runtime is not required anymore
* Feature: Windows: WebView2 is optional. Run UI in the default web browser if WebView2 was not installed
* Fix: Unable to connect to IpV6 supported site on chrome when server IpV6 is not configured
* Fix: Hold some TCP connections
* Fix: The client tries to connect to the IPv6 endpoint regardless of its connectivity
* Fix: Show Blank screen
* Update: Restore auto-reconnect
* Update: Improve performance and memory usage
* Update: Windows x86 (32-bit) is not supported anymore

### Server
* Feature: Report IPv6 support to client
* Feature: Add -domain to File AccessManager to set access-key endpoint will set to certificate domain
* Fix: Update Script doesn't work
* Fix: Hold some TCP connections
* Fix: Delay in showing command-line helps for File Access Server
* Fix: "Sequence contains no elements" Error when could not find any Public IP
* Update: Improve performance and memory usage
* Update: Improve Logging
* Update: Change config JSON property name for SessionOptions and TrackingOptions

# v2.5.323
### Client
* Update: Improve messages of disconnection reason
* Feature: Replace Always ON with auto-reconnect
* Fix: Anonymize VpnHood Server IP in diagnose  
* Fix: Windows Installer

### Server
* Update: Improve Log for AccessManager API CALL
* Update: Port Tracker
* Update: Improve session recovery
* Fix: Critical bug that consume much resources

# v2.4.321
### Server
* Update: Remove extra trace log from OS

# v2.4.320
### Client
* Update: Upgrade to .NET 7

### Server
* Feature: Compile as a self-contained; No need for .Net Framework Runtime
* Update: Upgrade to .NET 7
* Update: New Installation For Linux 
* Update: New Installation For Windows Server
* Update: New Installation For Docker
* Update: Improve logging
* Update: Removing App Launcher project
* Fix: Error on Windows Server. unsupported option or level was specified in a getsockopt or setsockopt call
* Fix: Archiving the log file when another instance of the server is already running
* Fix: Preventing running multiple instances from once location

# v2.4.318
### Client
* Feature: Show a message a device disconnected by your device
* Feature: Android TV support
* Update: Updating IP Location Database
* Update: Improve Client Battery Usage
* Update: Show SupportId (sid) to servers list
* Update: Remove Legacy AccessKey support
* Fix: Randomly select previous profile in UI

# v2.4.310
### Client
* Update: Removing Google Ads

# v2.4.307
### Client
* Feature: Add basic advertising support. Ouch!
* Update: Upgrade to android 12.1

# v2.4.304
### Client
* Fix: Trimming AccessKey
* Update: Improve detecting countries

### Server
* Fix: Nlog doesn't log some events
* Fix: Docker Installation on ubuntu
* Update: Add destination port in tracking

# v2.4.303
### Client
* Update: Simplify Client's Country exclusion

### Server
* Update: Improve Session Management

### Developer
* Update: Move VpnHood.Client.WebUI to a standalone repo

# v2.4.299
### Client
* Fix: Windows: Installation Package

# v2.4.297
### Server
* Fix: Reporting Negative usage

# v2.4.296
### Client
* Fix: Windows: WebView2 could not be installed on some devices

### Server
* Feature: Add Linux docker package
* Update: Sync all active sessions to access the server every few minutes
* Fix: Maintenance mode detection
* Fix: Synching sessions to access server on shut down

# v2.4.295
### Client
* Update: Tune TCP connections for games
* Fix: Error when setting PacketCapture include filter

### Server
* Feature: Server sends its last config error to access server
* Fix: TcpHost is already Started error
* Fix: Linux installation on some distribution
* Fix: LogLevel.Trace in DiagnoseMode

# v2.4.292
### Client
* Update: Improve stability and memory usage

### Server
* Update: Use keep-alive for TCP timeout
* Fix: Double Configure at startup
* Fix: Sending multiple requests to access server for session recovery
* Fix: Memory leak! Some dead sessions remain in memory
* Fix: Memory leak! TcpProxy remains in memory when just one peer has gone
* Fix: Memory leak! UdpProxy remains in memory
* Fix: Unusual Thread creation
* Fix: UDP Packet loss

# v2.3.291
### Client
* Fix: Android: Improve performance and stability in Android
* Fix: Add time-stamp to logger

### Server
* Update: Move Sessions options to AccessManager via ServerConfig
* Fix: Catch a lost packet when removing TcpDatagramChannel

# v2.3.290
### Client
* Fix: Crash on Android 12

### Server
* Feature: LocalPort and ClientIP Tracking Options
* Update: Set default port for -ep command
* Update: Use NLog.config in app binary folder if it does not exists in working folder

# v2.3.289
### Client
* Update: Add Logging Policy Warning
* Update: Create Private Server Link

### Server
* Update: Linux: Some issue in installation
* Fix: Maintenance mode detection

# v2.3.287
### Client
* Update: Upgrade to .NET 6
* Update: Diagnose just check some HTTPS sites to check internet connectivity
* Update: Windows: Disable right click on App WebView
* Fix: Not a valid calendar for the given culture

### Server
* Update: Upgrade to .NET 6
* Update: Configuration by access server
* Feature: Close session faster by handling client bye request
* Fix: Redact IP addresses in the log 

# v2.2.283
### Client
* Feature: Allow to have multi-endpoints in AccessToken
* Feature: Create IPv6 tunnel when a client has access to a server by IPv6
* Feature: Add "Exclude Local Network" to UI settings
* Fix: UDP Channel

### Server
* Feature: Dynamic configuration from AccessManager
* Feature: Multi listeners for different EndPoints
* Fix: Few bug in disposing
* Fix: Linux: systemctl restart VpnHoodServer 

# v2.1.276
* Feature: IPv6 Support
* Fix: Some packet loss in ping 

# v2.1.276
* Feature: IPv6 Support
* Fix: Some packet loss in ping 

# v2.0.272
* Feature: Block all IPv6 Global Unicast to prevent leak 
* Fix: Android: Vpn Connection keeps open after disconnecting
* Fix: Android: Crash in android 5.1
* Fix: IpFilter miss some IPs of countries
* Update: Improve the speed of establishing the connection

# v2.0.271
### Client
* Feature: Server Redirection
* Feature: Server Maintenance mode detection
* Feature: Validate packets integrity in UdpChannel
* Update: Android: Hide notification icon on the lock screen
* Update: Improve Performance and Memory usage
* Change: Stop supporting the old version
* Fix: Instability in reconnecting and disconnecting
* Fix: IpFilter didn't work properly when more than one country was selected
* Fix: Android: System Notification remain connected after disconnect
* Fix: Android: Some Apps were not shown in the AppFilter list (Require Permission: QUERY_ALL_PACKAGES)
* Fix: Android: Crash if a selected app in AppFilter does not exist anymore
* Fix: Android: Crash after disconnect

### Server
* Feature: Host Restart with REST access server (No UDP yet)
* Feature: Validate packets integrity in UdpChannel
* Update: Stop supporting the old version
* Update: Improve Performance and Memory usage
* Update: New REST AccessManager protocol
* Change: Stop supporting the old version

### Developer
* Update: Respect C# Nullable Reference Types
* Update: Mass Code cleanup
* Update: Decouple access manager from server to access server

# v1.3.254
### Client
* Feature: Android: Add Manage button to the system notification
* Fix: Casual packet loss!
* Fix: Empty error message after immediate disconnection
* Fix: Could not open the Protocol page
* Fix: Android: No window open by pressing menu items
* Fix: Windows: Could not load WinDivert

### Server
* Fix: Casual packet loss!

# v1.3.253
### Client
* Feature: IpFilter by countries
* Feature: Android: Exclude local networks from VPN
* Feature: Android: Add disconnect to device notification bar
* Update: Improve Performance and Memory usage
* Update: Reduce number of Public Server hints
* Fix: Windows: Didn't bypass Some local network traffics

### Server
* Update: Improve Performance and Memory usage

# v1.2.250
### Client
* Update: Display error for unsupported client
* Fix: Random Crash!
* Fix: No error message when Client lost the connection

### Server
* Update: Check session id for each UdpPacket
* Update: Reject unSupported client
* Fix: Updater on Linux
* Fix: Nlog maxArchiveDays maxArchiveFiles

# v1.2.249
* Feature: Reset apps TCP connections immediately after VPN get connected
* Update: Significantly optimize performance & stability
* Update: Improve power usage

### Client
* Fix: Attempting to connect after stopping the VPN

# v1.2.248
### Client
* Feature: Windows 7 Support
* Feature: Add "What's New" link in the main menu
* Fix: Windows: Display Main window location depending on TaskBar position
* Fix: Freeze network after auto reconnect
* Fix: Freeze network when UDP connection lost
* Fix: Freeze network after network lost
* Fix: Selecting current active server causes disconnection

### Developer
* Fix: Public Server in Android Sample

# v1.2.247
* Feature: Add UDP Protocol
* Update: Improve datagram performance
* Update: Improve overall performance
* Update: Improve messaging security
* Update: Improve Stability
* Fix: Problem in sending some UDP packets
* Fix: Json length is too big

### Developer
* Upgrade to SharpPcap 6.0

# v1.1.242
### Client
* Update: Windows: Installer check for new updates before installation

# v1.1.241
### Client
* Fix: Freeze in Disconnecting state
* Fix: Reconnection

# v1.1.240
### Client
* Fix: Diagnostic report "No Internet", when there is internet 
* Update: Windows: Change Updater

# v1.1.238
### Client
* Feature: Set allowed or disallowed Apps that can use VPN
* Update: Windows & Linux: Check TargetFramework before update
* Update: Show warning for Public Server

# v1.1.236
### Client
* Fix: Android: Crash when sending feedback on Android 11
* Fix: Connection already in progress error when changing server
* Update: Show traffic speed

### Server
* Update: Auto restart if VpnHoodServer stops unexpectedly
* Fix: Typo error in default.pfx filename for FileAccessManager
* Fix: Linux: Stop working after server update

# v1.1.235
### Client
* New: New public server
* New: Windows: Bypass local network from tunneling

# v1.1.232
### Client
* New: Android: Prevent landscape orientation
* Update: Significantly improve speed and stability
* Update: Automatically remove profiles when token does not exist
* Update: add some log EventId
* Fix: UDP loss in mass UDP traffic

### Server
* New: Send ClientVersion to AccessManager
* Update: drop Hello version 1 support
* Update: Significantly improve speed and stability
* Update: Automatically remove profiles when token does not exist
* Update: add some log EventId
* Fix: token is ignored when created by FileAccessManager
* Fix: UDP loss in mass UDP traffic

# v1.1.217
### Server
* New: Rest server validate Self-Signed certificates by RestCertificateThumbprint property in appsettings

# v1.1.216
* New: Updater has completely changed

### Server
* New: Add stop command to stop all server instance
* New: Linux: Add installation script
* New: Linux: Run server as a service
* Change: rename "run" command to "start"

# v1.1.202
### Client
* New: Change server list page
* New: Android: Change system status bar color to match UI
* New: Windows: Change icon on notification area by connection status
* Fix: Big UI on some devices
* Update: Change Public Server Name

### Server
* Update: Start new log file on every run

# v1.1.197
### Client
* Fix: rejecting AccessKey with vh://

### Server
* New: Report Linux Distribution info
* New: Report connected ClientVersion
* Fix: "Permission Denied" error in Linux while sending some UDP packets

# v1.1.195
### Client
* Feature: Modern UI
* Feature: Show usage if there is any limitation
* Feature: Windows: reconnect last connection after auto update
* Fix: Windows: Fix main window size
* Fix: Windows: launch application after installation

### Server
* Fix: Use last command line argument after auto update

# v1.1.187
### Client
* Feature: Windows: Use new standalone UI
* Feature: Windows: Add Context menu to system tray
* Update: Add Microsoft WebView2 Edge to Windows Installer prerequisites
* Update: Send ClientVersion to server
* Fix: AccessKey prefix

### Server
* Fix: Reading server port number from appsettings.json
* Update: Support multiple public IP and Amazon ElasticIP

# v1.1.184
### Client
* Feature: Auto Configure Windows Defender Firewall
* Update: Improve diagnosing
* Fix: Significantly Improve connection stability & speed
* Fix: Displaying connection state

### Server
* Fix: Unhandled NullReferenceException on ping packets
* Fix: Improve server memory cleanup
* Fix: Prevent new connection after session disposed
* Fix: Speed Monitor and connection idle state
* Fix: Improve connection stability and lost packets
* Feature: ICMP logging for client and server with IsDiagnoseMode
* Feature: Use NLog for logging
* Feature: Auto initialize NLog config and appsettings.json

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
* Fix: Checking update from the Internet

### Server
* Update: add subdomain when creating self-signed certificates with random CN

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
