# VpnHood.AccessServer

## Installation On Windows
1. Create windows user: AccessServerAgent
1. Create Database with Case Sensitive from template
   1. CALL: EXEC dbo.Init
   1. In Database security create loggin for AccessServerAgent
   1. Grant db_datareader & db_datawrite to AccessServerAgent for the created Database
1. Extract VpnHood.AccessServer to a folder such as: **C:\App\VpnHood.AccessServer**
   1. Copy settings file (appsettings.json) next to **run.exe**
   1. Set **AuthProviders** in settings; VpnHood.AccessServer.Cmd can create **SymmetricSecurityKey**
   1. Set **Url**, sample: **https://0.0.0.0:9090**
   1. Set **Certificate**, Sample: <br>
   `"Certificate": {"Subject": "access.vpnhood.com", "Store": "MY", "Location": "LocalMachine", "AllowInvalid": true}` or
   `"Certificate": { "Path": "access-server.pfx", "Password": "" }`
