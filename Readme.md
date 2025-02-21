
1. Get access to NPE Tenant
1. Install .net
![alt text](image-3.png)

1. Create new .net project 
![alt text](image.png)
2. Select Console Project
![alt text](image-1.png)
3. Name Project ![alt text](image-2.png)
4. cd SingleAgent
4. dotnet add package Microsoft.SemanticKernel
5. dotnet add package Azure.Identity
5.  dotnet add package Microsoft.SemanticKernel.Agents.Core --prerelease
5. dotnet add package Azure.AI.Projects --prerelease
5. dotnet add package OpenTelemetry.Exporter.Console
5. Add editorconfig
6. az login