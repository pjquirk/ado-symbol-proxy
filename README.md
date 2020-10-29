# ADO Symbol Proxy
Inspired by [this comment](https://github.com/microsoft/perfview/issues/1064#issuecomment-570438042).

I couldn't get Windows Performance Analyzer to recognize the Azure DevOps symbol server due to 
1. Authentication required
1. Redirect responses

This server handles both problems by posing as a symbol server that proxies requests to Azure DevOps.
It makes requests to Azure DevOps' symbol server endpoint using a PAT, and then follows any redirects
so it can send the bits back directly.

# Usage
1. Set the `ADO_PAT` environment variable with your PAT.
1. Build/run this project.
1. Add the proxy's URL to your symbol path, e.g. `srv*C:\Symbols*https://localhost:44340/api/symbols/download`.
1. Reload symbols in WPA/PerfView/WinDbg/Visual Studio.