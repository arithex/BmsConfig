# BmsConfig
New Config tool for Falcon BMS -- replacement for the legacy F4Patch-based Config.exe

### Tenets

- Modify 'Falcon BMS User.cfg' and 'Falcon BMS VR.cfg'
- Treat 'Falcon BMS.cfg' mostly as a readonly manifest/catalog of available cfg params (except for the feature-flag params)
- Be an editor not a generator -- whitepace, comments and additional/undocumented params in User.cfg remain, unharmed
- Play nice with the Launcher -- don't add or modify below the Launcher's cut-line
