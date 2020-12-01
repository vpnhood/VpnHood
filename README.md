# VpnHood.AccessServer

#Installation On Windows
1. Create windows user: AccessServerAgent
1. Create Database with Case Sensitive from template
   1. CALL: EXEC dbo.Init
   1. In Database security create loggin for AccessServerAgent
   1. Grant db_datareader & db_datawrite to AccessServerAgent for the created Database
