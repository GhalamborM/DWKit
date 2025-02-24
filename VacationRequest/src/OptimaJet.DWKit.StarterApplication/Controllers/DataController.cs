using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NUglify.Helpers;
using OptimaJet.DWKit.Application;
using OptimaJet.DWKit.Core;
using OptimaJet.DWKit.Core.Model;
using OptimaJet.DWKit.Core.View;
using OptimaJet.Workflow.Core.Runtime;

namespace OptimaJet.DWKit.StarterApplication.Controllers
{
    //[Authorize(IdentityServer4.IdentityServerConstants.LocalApi.PolicyName)]
    [Authorize]
    [Route("data")]
    public class DataController : Controller
    {
        [AllowAnonymous]
        [Route("get")]
        public async Task<ActionResult> GetData(string name, string propertyName, string urlFilter, string options,
            string filter, string paging, string sort, bool forCopy = false, bool mobile = false, string schemeName = null)
        {
            try
            {
                if (!await DWKitRuntime.Security.CheckFormPermissionAsync(name, "View"))
                {
                    return new JsonResult(new FailResponse("Access denied!")) { StatusCode = 401 };
                }

                var getRequest = CreateGetRequest(name, propertyName, urlFilter, options, filter, paging, sort, forCopy,
                    mobile, schemeName);
                var data = await DataSource.GetDataForFormAsync(getRequest).ConfigureAwait(false);
                if (data.IsFromUrl && FailResponse.IsFailResponse(data.Entity, out FailResponse fail))
                {
                    return Json(fail);
                }

                var result = data.Entity != null ? data.Entity.ToDictionary(true) : new object();
                return Json(new ItemSuccessResponse<object>(result));
            }
            catch (Exception e)
            {
                return Json(new FailResponse(e));
            }
        }

        [AllowAnonymous]
        [Route("change")]
        [HttpPost]
        public async Task<ActionResult> ChangeData(string name, string data, bool mobile = false, string schemeName = null)
        {
            try
            {
                if (!await DWKitRuntime.Security.CheckFormPermissionAsync(name, "Edit"))
                {
                    return new JsonResult(new FailResponse("Access denied!")) { StatusCode = 401 };
                }

                var postRequest = new ChangeDataRequest(name, data)
                {
                    Mobile = mobile,
                    SchemeName = schemeName,
                    BaseUrl = $"{Request.Scheme}://{Request.Host.Value}",
                    GetHeadersForLocalRequest = () =>
                    {
                        var dataUrlParameters = new Dictionary<string, string>();
                        dataUrlParameters.Add("Cookie",
                            string.Join(";",
                                Request.Cookies.Select(c => $"{c.Key}={c.Value}")));

                        Request.Headers.Where(c=> c.Key == "Authorization")
                            .ForEach(c=> dataUrlParameters.Add(c.Key, c.Value));
                        return dataUrlParameters;
                    }
                };

                var res = await DataSource.ChangeData(postRequest);
                if (res.success != null)
                {
                    return Json(res.success);
                }

                return Json(res.fail);
            }
            catch (Exception e)
            {
                return Json(new FailResponse(e));
            }
        }

        [Route("delete")]
        [HttpPost]
        public async Task<ActionResult> DeleteData(string name, string propertyName, string data, bool mobile = false, string schemeName = null)
        {
            try
            {
                if (!await DWKitRuntime.Security.CheckFormPermissionAsync(name, "Edit"))
                {
                    return new JsonResult(new FailResponse("Access denied!")) { StatusCode = 401 };
                }

                var deleteRequest = new ChangeDataRequest(name, data, propertyName)
                {
                    Mobile = mobile,
                    SchemeName = schemeName,
                    BaseUrl = $"{Request.Scheme}://{Request.Host.Value}",
                    GetHeadersForLocalRequest = () =>
                    {
                        var dataUrlParameters = new Dictionary<string, string>();
                        dataUrlParameters.Add("Cookie",
                            string.Join(";",
                                Request.Cookies.Select(c => $"{c.Key}={c.Value}")));
                        Request.Headers.Where(c=> c.Key == "Authorization")
                            .ForEach(c=> dataUrlParameters.Add(c.Key, c.Value));
                        return dataUrlParameters;
                    }
                };

                var res = await DataSource.DeleteData(deleteRequest);
                if (res.success != null)
                    return Json(res.success);
                return Json(res.fail);
            }
            catch (Exception e)
            {
                return Json(new FailResponse(e));
            }
        }

        [AllowAnonymous]
        [Route("dictionary")]
        public async Task<ActionResult> GetDictionary(string name, string sort, string columns, string paging, string filter, string parent)
        {
            try
            {
                var data = await DataSource.GetDictionaryAsync(name, sort, columns, paging, filter, parent).ConfigureAwait(false);

                var result = NotNullOrEmpty(parent)
                    ? new ItemSuccessResponse<IEnumerable<object>>(data.Item1.Select(x => new { Id = x.Item1, Name = x.Item2, Parent = x.Item3, HasChild = x.Item4, Values = x.Item5 }))
                    : new ItemSuccessResponse<IEnumerable<object>>(data.Item1.Select(x => new { Key = x.Item1, Value = x.Item2, Values = x.Item5 }));

                result.Count = data.Item2;

                return Json(result);
            }
            catch (Exception e)
            {
                return Json(new FailResponse(e));
            }
        }

        [HttpPost]
        [Route("upload")]
        public async Task<ActionResult> UploadFile()
        {
            try
            {
                if (Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files[0];
                    var properties = new Dictionary<string, object>
                    {
                        { Core.DataProvider.FileProperties.Name, file.FileName },
                        { Core.DataProvider.FileProperties.ContentType, file.ContentType }
                    };
                    var stream = file.OpenReadStream();
                    var token = await DWKitRuntime.ContentProvider.AddAsync(stream, properties);

                    var props = await DWKitRuntime.ContentProvider.GetPropertiesAsync(token);

                    return Json(new SuccessResponse(new { properties = props }));
                }
            }
            catch (Exception ex)
            {
                return Json(new FailResponse(ex));
            }

            return Json(new FailResponse("No any files in the request!"));
        }

        [Route("download/{token}")]
        public async Task<ActionResult> DownloadFile(string token)
        {
            var data = await DWKitRuntime.ContentProvider.GetAsync(token);
            var properties = data.Properties;
            var stream = data.Stream;

            var filename = "unknown";
            var contentType = "application/unknown";

            if (properties != null)
            {
                if (properties.ContainsKey("Name") && properties["Name"] != null)
                {
                    filename = properties["Name"];
                }

                if (properties.ContainsKey("ContentType") && properties["ContentType"] != null)
                {
                    contentType = properties["ContentType"];
                }
            }
            return File(stream, contentType, filename);
        }

        [AllowAnonymous]
        [Route("export")]
        public async Task<IActionResult> ExportData(string name, string propertyName, string urlFilter, string options,
            string filter, string paging, string sort, string cols, string fileName, string pagerType, bool forCopy = false, bool mobile = false)
        {
            if (!await DWKitRuntime.Security.CheckFormPermissionAsync(name, "View"))
            {
                return new JsonResult(new FailResponse("Access denied!")) { StatusCode = 401 };
            }

            bool isServerPager = pagerType == "server";

            GetDataRequest getRequest;
            if (isServerPager)
            {
                getRequest = CreateGetRequest(name, propertyName, urlFilter, options, filter, paging, sort, forCopy, mobile);
            }
            else
            {
                getRequest = CreateGetRequest(name, propertyName, urlFilter, options, filter, paging, null, forCopy, mobile);
            }

            var data = await DataSource.GetDataForFormAsync(getRequest).ConfigureAwait(false);


            if (data.IsFromUrl && FailResponse.IsFailResponse(data.Entity, out FailResponse fail))
            {
                return Json(fail);
            }

            var resultFileName = string.Format("{0}.xlsx", propertyName);
            if (NotNullOrEmpty(fileName))
            {
                resultFileName = fileName;
            }

            List<ClienSortItem> extraSort = null;
            if (!isServerPager && NotNullOrEmpty(sort))
            {
                extraSort = JsonConvert.DeserializeObject<List<ClienSortItem>>(sort);
            }

            var stream = DataSource.ExportToExcel(CreateColsFilter(cols), propertyName, data.Entity, extraSort);
            var mimeType = "application/vnd.ms-excel";
            return File(stream, mimeType, resultFileName);
        }

        private static bool NotNullOrEmpty(string urlFilter)
        {
            return !string.IsNullOrEmpty(urlFilter) && !urlFilter.Equals("null", StringComparison.OrdinalIgnoreCase);
        }

        private GetDataRequest CreateGetRequest(string name, string propertyName, string urlFilter, string options,
            string filter, string paging, string sort, bool forCopy, bool mobile, string schemeName = null)
        {
            string filterActionName = null;
            string idValue = null;
            var filterItems = new List<ClientFilterItem>();

            if (NotNullOrEmpty(urlFilter))
            {
                try
                {
                    filterItems.AddRange(JsonConvert.DeserializeObject<List<ClientFilterItem>>(urlFilter));
                }
                catch
                {
                    var filterActions = DWKitRuntime.ServerActions.GetFilterNames().Where(n => n.Equals(urlFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                    string filterAction = null;
                    filterAction = filterActions.Count == 1 ? filterActions.First()
                        : filterActions.FirstOrDefault(n => n.Equals(urlFilter, StringComparison.Ordinal));

                    if (!string.IsNullOrEmpty(filterAction))
                        filterActionName = filterAction;
                    else
                    {
                        idValue = urlFilter;
                    }
                }
            }

            if (NotNullOrEmpty(filter))
            {
                filterItems.AddRange(JsonConvert.DeserializeObject<List<ClientFilterItem>>(filter, new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None
                }));
            }


            var getRequest = new GetDataRequest(name)
            {
                SchemeName = schemeName,
                PropertyName = propertyName,
                FilterActionName = filterActionName,
                IdValue = idValue,
                Filter = filterItems,
                BaseUrl = $"{Request.Scheme}://{Request.Host.Value}",
                ForCopy = forCopy,
                Mobile = mobile,
                GetHeadersForLocalRequest = () =>
                {
                    var dataUrlParameters = new Dictionary<string, string>();
                    dataUrlParameters.Add("Cookie", string.Join(";",
                        Request.Cookies.Select(c => $"{c.Key}={c.Value}")));
                    Request.Headers.Where(c=> c.Key == "Authorization")
                        .ForEach(c=> dataUrlParameters.Add(c.Key, c.Value));

                    return dataUrlParameters;
                }
            };

            if (NotNullOrEmpty(options))
            {
                getRequest.OptionsDictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(options);
            }

            if (NotNullOrEmpty(paging))
            {
                getRequest.Paging = JsonConvert.DeserializeObject<ClientPaging>(paging);
            }

            if (NotNullOrEmpty(sort))
            {
                getRequest.Sort = JsonConvert.DeserializeObject<List<ClienSortItem>>(sort);
            }

            return getRequest;
        }

        private Dictionary<string, string> CreateColsFilter(string cols)
        {
            var colPairs = (NotNullOrEmpty(cols) ? cols : string.Empty).Split(',').Select((s) =>
            {
                var kvPair = s.Split(':');

                return new KeyValuePair<string, string>(kvPair[0], kvPair[1]);
            });

            return new Dictionary<string, string>(colPairs);
        }
    }
}
