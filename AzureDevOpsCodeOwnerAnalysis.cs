using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using DotNet.Globbing;

namespace AzureDevOps.Community
{
    public static class AzureDevOpsCodeOwnerAnalysis
    {
        [FunctionName("AzureDevOpsCodeOwnerAnalysis")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            try
            {
                // Deserialize the Pull request
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);        

                // Apply CodeOwners to PR
                PullRequestState state = CreatePullRequestState(data, log);
                if (state != null)
                {
                    await ApplyCodeOwnersToPR(state);
                }

                return new OkResult();
            }
            catch(Exception ex)
            {
                log.LogError(ex.ToString());
                return new InternalServerErrorResult();
            }
        }
        private static async Task ApplyCodeOwnersToPR(PullRequestState state)
        {
            // Create API to Azure DevOps
            AzureDevOpsAPI api = new AzureDevOpsAPI(state);

            // See if this repository and branch have a CODEOWNERS file
            var codeOwnersExists = await api.CodeOwnersExists();
            if (codeOwnersExists)
            {
                // Find all files changed across all PR commits for this PR
                var allChanges = new List<string>();
                var commits = await api.GetPRCommits();
                foreach(var commitId in commits)
                {
                    List<string> changes = await api.GetPRCommitChanges(commitId);
                    foreach(string change in changes)
                    {
                        if (!allChanges.Contains(change))
                        {
                            allChanges.Add(change);
                        }
                    }
                }

                // Get the Code Owner file and determine impacted code owners and current reviewers
                string codeOwnerFile = string.Empty;
                codeOwnerFile = await api.DownloadCodeOwnersFile();
                var impactedCodeOwners = GetCodeOwnerMatches(allChanges, codeOwnerFile, state.Log);
                var reviewers = await api.GetPRReviewers();

                // Assign any missing code owners as reviewers
                foreach(var owner in impactedCodeOwners)
                {
                    string userId = await api.LookupUserId(owner);
                    if (String.IsNullOrWhiteSpace(userId))
                    {
                        state.Log.LogInformation($"Can't Add/Remove Reviewer {owner} NO USER ID!!");
                    }
                    else 
                    {
                        // existing reviewers are removed (in case they defered or were made option prior)
                        // this forces re-evaluation on update
                        if (reviewers.Contains(owner))
                        {
                            await api.RemovePRReviewer(userId);
                        }
                        await api.AddPRReviewer(userId);
                    }
                }
            }
        }
        private static PullRequestState CreatePullRequestState(dynamic data, ILogger log)
        {
            int pullRequestId;
            if (!int.TryParse(data.resource.pullRequestId.ToString(), out pullRequestId))
            {
                return null;
            }

            string repositoryName = data.resource.repository.name;
            string projectName = data.resource.repository.project.name;
            string branch = data.resource.sourceRefName;
            branch = branch.Substring(11); // Remote refs/heads/
            string projectUrl = data.resource.repository.project.url;
            string organizationUrl = DeriveOrganizationUrl(projectUrl);
            string organization = DeriveOrganization(projectUrl);

            return new PullRequestState
            {
                Organization = organization,
                OrganizationUrl = organizationUrl,
                Project = projectName,
                Branch = branch,
                Repository = repositoryName,
                PullRequestId = pullRequestId,
                Log = log
            };
        }
        private static string DeriveOrganizationUrl(string projectUrl)
        {
            string [] urlParts = projectUrl.Substring(8).Split('/');
            if (urlParts.Length > 2)
            {
                if (urlParts[0].Equals("dev.azure.com"))
                {
                    return $"https://dev.azure.com/{urlParts[1]}";
                }
                else
                {
                    return $"https://{urlParts[0]}";
                }
            }
            return string.Empty;
        }
        private static string DeriveOrganization(string projectUrl)
        {
            string [] urlParts = projectUrl.Substring(8).Split('/');
            if (urlParts.Length > 2)
            {
                if (urlParts[0].Equals("dev.azure.com"))
                {
                    return urlParts[1];
                }
                else
                {
                    return urlParts[0];
                }
            }
            return string.Empty;
        }
        private static List<string> GetCodeOwnerMatches(List<string> changes, string codeOwners, ILogger log)
        {
            List<Tuple<string, List<string>>> codeOwnerLines = new List<Tuple<string, List<string>>>();
            GlobOptions options = new GlobOptions();
            List<string> impactedCodeOwners = new List<string>();

            options.Evaluation.CaseInsensitive = true;

            using (StringReader sr = new StringReader(codeOwners)) 
            {
                string line;
                while ((line = sr.ReadLine()) != null) 
                {
                    if (!String.IsNullOrWhiteSpace(line) && !line.Trim().StartsWith("#"))
                    {
                        string [] lineparts = line.Split(new char [] { ' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                        if (lineparts.Length > 1)
                        {
                            string globPattern = lineparts[0];
                            if (!globPattern.StartsWith("/"))
                            {
                                globPattern = $"/{globPattern}";
                            }

                            List<string> currentPatternOwners = new List<string>();
                            for(int u = 1; u < lineparts.Length; u++)
                            {
                                currentPatternOwners.Add(lineparts[u]);
                            }

                            if (currentPatternOwners.Count > 0)
                            {
                                codeOwnerLines.Add(new Tuple<string, List<string>>(globPattern, currentPatternOwners));
                            }
                        }
                    }
                }
            }

            foreach(string change in changes)
            {
                for(int g = codeOwnerLines.Count -1; g >= 0; g--)
                {
                    bool match = Glob.Parse(codeOwnerLines[g].Item1, options).IsMatch(change);             
                    if (match)
                    {
                        foreach(string owner in codeOwnerLines[g].Item2)
                        {
                            if (!impactedCodeOwners.Contains(owner))
                            {
                                impactedCodeOwners.Add(owner);
                            }
                        }
                        break; // We apply matches later in the file and stop processing owners for a change once we find one. 
                    }
                }
            }

            return impactedCodeOwners;
        }
    }
}
