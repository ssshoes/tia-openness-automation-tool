# TIA Openness Automation Tool

Windows desktop application for automating engineering tasks in Siemens TIA Portal V19 through the TIA Portal Openness API.

## Features

- import Lenze drives, Pepperl masters and industrial cameras from CSV,
- configure PLC security and UMAC roles,
- start and open TIA Multiuser sessions,
- generate conveyor logic in LAD from CSV and an XML template,
- export PLC blocks to XML.

## Requirements

- Windows,
- Siemens TIA Portal V19 with Openness enabled,
- .NET Framework 4.8,
- Visual Studio with the .NET desktop development workload.

The project references the Siemens Openness assemblies from the default TIA Portal V19 installation directory:

`C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19`

## Credentials

No passwords are stored in the repository. Before using the PLC security option, define these environment variables locally:

- `TIA_AMSORT_USER_PASSWORD`
- `TIA_OPC_UA_USER_PASSWORD`

Use different strong passwords for the two accounts. Do not commit their values.

