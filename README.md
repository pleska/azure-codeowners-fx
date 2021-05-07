# azure-codeowners-fx

__A function to apply required pull request reviewers to an Azure DevOps Pull Request based on a CODEOWNERS file at root.__

This project is a C# dotnet core 3.1 Azure Function that can be hooked to Azure DevOps and will apply the CODEOWNERS file as pull request reviewers. The [CODEOWNERS file format](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners) is defined by github. 

## Setup 

To implement this function a couple of things are required. 

1. Deploy the Azure function to a resource group.
2. Obtain a Azure DevOps personal access token and store it as a secret in a Azure Key vault. 
3. Add a azure function "Application settings" entry named `AzDoPAT` and set the value to the secret using the function binding syntax like...

```
@Microsoft.KeyVault(VaultName=yourakvname;SecretName=yourPATsecretname)
```

4. In the azure function enable the `Identity`, `System assigned` identity feature. 
5. In the key vault grant your azure function access `Get` and `List` to the `Secret Permissions`.
6. In Azure DevOps under Project, `Service Hooks` add two Web Hooks calling your function
    - Pull request created: https://yourfxname.azurewebsites.net/api/AzureDevOpsCodeOwnerAnalysis?code=yourfunctioncode
    - Pull request updated: https://yourfxname.azurewebsites.net/api/AzureDevOpsCodeOwnerAnalysis?code=yourfunctioncode

## Code Owners

To setup a code owners processing for your project create a file named CODEOWNERS at the root. Each line starts with the pattern to match when applying owners. It supports directory glob patterns like /**/xyz*.md indicating any level of directory depth. After this list one or more user login e-mail addresses for valid users in the Azure DevOps organization. 

```
* user1@domain.com
/**/*.cs csharpexpert@domain.com
/**/*.png graphicartist@domain.com
```