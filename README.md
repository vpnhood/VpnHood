# VpnHood.AccessServer

## Installation On Windows
1. Create windows user: **AccessServerAgent**
   1. Go to "Administrative Tools -> Local Security Policy -> Local Policies -> User Rights Assignment -> Log on as a batch job" and add **AccessServerAgent** user

1. Add server sertificate (.pfx) to local machine
   1. In "Computer Sertificate -> Personal -> Your Certificate -> Manage Private Key" and grant read access to **AccessServerAgent**

1. Create Database with Case Sensitive from template
   1. CALL: `EXEC dbo.Init`
   1. In Database security create loggin for AccessServerAgent
   1. Grant **db_datareader** & **db_datawrite** to AccessServerAgent for the created Database
   
1. Extract VpnHood.AccessServer to a folder such as: **C:\App\VpnHood.AccessServer**
   1. Copy settings file (appsettings.json) next to **run.exe**
   1. Set **AuthProviders** in settings; VpnHood.AccessServer.Cmd can create a **SymmetricSecurityKey**
   1. Set **Url**, sample: https://0.0.0.0:9090
   1. Set **Certificate**, Sample: <br>
   `"Certificate": {"Subject": "access.vpnhood.com", "Store": "MY", "Location": "LocalMachine", "AllowInvalid": true}` or
   `"Certificate": { "Path": "access-server.pfx", "Password": "" }`
   
1. Run the following command to create the auto start task in task scheduler <br>
   `SCHTASKS /create /f /tn VpnHood.AccessServer /sc onstart /tr "C:\Apps\VpnHood.AccessServer\run.exe /nowait" /ru VH1\AccessServerAgent /rp password`
