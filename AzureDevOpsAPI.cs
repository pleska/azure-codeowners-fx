namespace AzureDevOps.Community
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System.Linq;
    using AzureDevOps.Community.Model;

    public class AzureDevOpsAPI
    {
        private PullRequestState State;
        public AzureDevOpsAPI(PullRequestState currentState)
        {
            this.State = currentState;
        }

        public async Task<List<string>> GetPRReviewers()
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/pullRequests/{State.PullRequestId}/reviewers?api-version=6.0";
            List<string> reviewers = new List<string>();

            try
            {
                using (var client = new HttpClient())
                {
                    string response = await HttpGet(Url);
                    AzDoAPICollection<AzDoAPIReviewer> data = JsonConvert.DeserializeObject<AzDoAPICollection<AzDoAPIReviewer>>(response);
                    reviewers = data.value.Select(c => c.uniqueName).ToList();
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return reviewers;
        }
        public async Task<List<string>> GetPRCommits()
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/pullRequests/{State.PullRequestId}/commits?api-version=6.0";
            List<string> commitIds = new List<string>();
            try
            {
                string response = await HttpGet(Url);
                AzDoAPICollection<AzDoAPICommit> data = JsonConvert.DeserializeObject<AzDoAPICollection<AzDoAPICommit>>(response);
                commitIds = data.value.Select(c => c.commitId).ToList();
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return commitIds;
        }
        public async Task<List<string>> GetPRCommitChanges(string commitId)
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/commits/{commitId}/changes?top=1000&skip=0&api-version=6.0";
            List<string> changes = new List<string>();
            try
            {
                string response = await HttpGet(Url);
                AzDoAPIChangeCollection data = JsonConvert.DeserializeObject<AzDoAPIChangeCollection>(response);
                foreach (var change in data.changes)
                {
                    changes.Add(change.item.path);
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return changes;
        }
        public async Task<bool> CodeOwnersExists()
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/items?scopePath=/&recursionLevel=OneLevel&versionDescriptor.version={State.Branch}&api-version=6.0";
            try
            {
                var response = await HttpGet(Url);
                AzDoAPICollection<AzDoAPIGitItem> data = JsonConvert.DeserializeObject<AzDoAPICollection<AzDoAPIGitItem>>(response);
                foreach (var item in data.value)
                {
                    if (item.path.ToUpper().Contains("CODEOWNERS"))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return false;
        }
        public async Task<string> DownloadCodeOwnersFile()
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/items?scopePath=/CODEOWNERS&includeContent=true&download=false&versionDescriptor.version={State.Branch}&api-version=6.0";
            try
            {
                var response = await HttpGet(Url, "text/plain");
                return response;
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return string.Empty;
        }
        public async Task<String> LookupUserId(string userName)
        {
            string userNameFilter = $"name+eq+%27{userName}%27";
            string Url = $"https://vsaex.dev.azure.com/{State.Organization}/_apis/userentitlements?$filter={userNameFilter}&api-version=6.0-preview.3";
            try
            {
                var response = await HttpGet(Url);
                AzDoEntitlements data = JsonConvert.DeserializeObject<AzDoEntitlements>(response);
                if (data.members.Length > 0 && (
                    String.Equals(data.members[0].user.principalName, userName, StringComparison.InvariantCultureIgnoreCase) ||
                    String.Equals(data.members[0].user.mailAddress, userName, StringComparison.InvariantCultureIgnoreCase) ||
                    String.Equals(data.members[0].user.directoryAlias, userName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return data.members[0].id;
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return string.Empty;
        }
        public async Task AddPRReviewer(string userId)
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/pullRequests/{State.PullRequestId}/reviewers/{userId}?api-version=6.0";
            try
            {
                AzDoAPIRequiredReviewer reviewer = new AzDoAPIRequiredReviewer
                {
                    isRequired =true
                };
                await HttpPut(Url, reviewer);
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
        }        
        public async Task RemovePRReviewer(string userId)
        {
            string Url = $"{State.OrganizationUrl}/{State.Project}/_apis/git/repositories/{State.Repository}/pullRequests/{State.PullRequestId}/reviewers/{userId}?api-version=6.0";
            try
            {
                // this needs no reviewer passed as the reviewer to remove is in the URL
                await HttpDelete<string>(Url);
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
        }
        private async Task<string> HttpGet(string Url, string encoding = "application/json")
        {
            var AzDoPAT = Environment.GetEnvironmentVariable("AzDoPAT", EnvironmentVariableTarget.Process);
            string credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{AzDoPAT}"));
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(encoding));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
                    var response = await client.GetStringAsync(Url);
                    return response;
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
            return string.Empty;
        }
        private async Task HttpPut<T>(string Url, T body, string encoding = "application/json")
        {
            var AzDoPAT = Environment.GetEnvironmentVariable("AzDoPAT", EnvironmentVariableTarget.Process);
            string credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{AzDoPAT}"));
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(encoding));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
                    var response = await client.PutAsJsonAsync<T>(Url, body);
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
        }       
        private async Task HttpDelete<T>(string Url, string encoding = "application/json")
        {
            var AzDoPAT = Environment.GetEnvironmentVariable("AzDoPAT", EnvironmentVariableTarget.Process);
            string credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{AzDoPAT}"));
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(encoding));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
                    var response = await client.DeleteAsync(Url);
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                State.Log.LogError(ex.ToString());
            }
        }
    }
}