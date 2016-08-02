# PSScriptExecHttp

A simple client/server tool for executing powershell script over http. You can run the tool as the server, which in turn will run the predefined powershell script `runscript.ps1` upon request. You can also use the same tool as a client to request a running server to execute the script.

## Prerequisite

* To build the tool, you need to download the required NuGet packages.
* To run the tool, you need to copy the provided sample `runscript.ps1` file to the same location as the executable.

## Display help information

```
PSScriptExecHttp.exe --help
```

## (Example) Run as a server

```
PSScriptExecHttp.exe --server
```

To terminate the server, press `Ctrl+C`.

## (Example) Run as a client tool

```
PSScriptExecHttp.exe --host localhost --url /runscript --query "param1:value1^param2:value2^param3:value3" --timeout 10000
```

The sample `runscript.ps1` file accepts three parameters, $Param1, $Param2, and $Param3, prints them and then returns the printed output back to client. You can edit the script however you want with different parameters. And to provide those parameters from the client, use the `--query` option.
