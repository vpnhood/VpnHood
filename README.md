# VpnHood.AccessServer

## Installation
1. Init the database to a local db
2. Sync the database to a remote with sql compare
3. Configure Connection in appsettings.json
   * set AuthProviders:SymmetricSecurityKey
   * set ConnectionStrings:VhDatabase
4. Publish

--- Old
 
1. Extract VpnHood.AccessServer to a folder such as: **C:\App\VpnHood.AccessServer**
   1. Set **Url**, sample: https://0.0.0.0:9090
   1. Set **Certificate**, Sample: <br>
   `"Certificate": {"Subject": "access.vpnhood.com", "Store": "MY", "Location": "LocalMachine", "AllowInvalid": true}` or
   `"Certificate": { "Path": "access-server.pfx", "Password": "" }`
   
# Create a linux sudo user
  1. adduser _username_
  2. usermod -aG sudo _username_
  3. userdel -r _username_
# grant access to linux user
  1. sudo chown -R _username_ *
