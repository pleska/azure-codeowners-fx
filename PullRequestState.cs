using Microsoft.Extensions.Logging;

namespace AzureDevOps.Community
{
    public class PullRequestState
    {
      public string OrganizationUrl;
      public string Organization;
      public string Project;
      public string Repository;
      public string Branch;
      public int PullRequestId;
      public ILogger Log;
    }
}