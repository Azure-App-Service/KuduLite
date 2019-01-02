using System;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Services.Diagnostics
{
    /// <inheritdoc />
    /// <summary>
    /// API endpoint to GET, Update and Delete Container network, memory, cpu usage and disk usage stats. 
    /// </summary>
    [Route("/api/stats")]
    public class ContainerInfoController : Controller
    {
        private static SiteInstanceStats _siteInstanceStats = new SiteInstanceStats();
        private static readonly object SiteInstStatsLock = new object();

        /// <summary>
        /// Updates all/particular stat for a container.
        /// Can only be called by an authorized metering service.
        /// <param name="containerId">ContainerId whose stats are to be purged</param>
        /// </summary>
        /// <param name="data">Valid JSON Object containing the data to be updated</param>
        /// <param name="containerId">ContainerId whose stats are to be updated</param>
        /// <param name="filterName">Filter for a container whose stats are to be updated, if null all the old stats are
        ///  purged and stats in this request are added</param>
        /// <returns></returns>
        [HttpPost("{containerId=all}/{filterName=all}")]
        public IActionResult UpdateLog(
            [FromBody] JObject data,
            string containerId,
            string filterName)
        {
            try
            {
                lock (SiteInstStatsLock)
                {
                    if (containerId.Equals("all"))
                    {
                        var siteInstStats =
                            Newtonsoft.Json.JsonConvert.DeserializeObject<Kudu.Core.Infrastructure.SiteInstanceStats>(
                                data.ToString(Formatting.None));
                        _siteInstanceStats = siteInstStats;
                    }
                    else
                    {
                        if (filterName.Equals("all"))
                        {
                            var cntInfo =
                                Newtonsoft.Json.JsonConvert.DeserializeObject<Kudu.Core.Infrastructure.ContainerInfo>(
                                    data.ToString(Formatting.None));
                            _siteInstanceStats.appContainersOnThisInstance[cntInfo.Id] = cntInfo;
                        }
                        else
                        {
                            // CORE TODO:                             
                        }
                    }

                    return Ok("Container stats updated");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    JsonConvert.SerializeObject(new ExceptionResponse(ex.Message, ex.StackTrace)));
            }
        }


        /// <summary>
        /// Deletes all the stats for a container.
        /// Can only be called by an authorized metering service.
        /// This happens when a container is killed/it stops running
        /// </summary>
        /// <param name="containerId">ContainerId whose stats are to be purged</param>
        [HttpDelete("{containerId}")]
        public IActionResult GetLog(string containerId)
        {
            try
            {
                lock (SiteInstStatsLock)
                {
                    if (!_siteInstanceStats.appContainersOnThisInstance.ContainsKey(containerId))
                        return BadRequest("ContainerId " + containerId + " not found.");
                    _siteInstanceStats.appContainersOnThisInstance.Remove(containerId);
                    return Ok("Stats updated successfully");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Returns a list of containers for a webapp and it's memory/cpu stats.
        /// This stats can be filtered by giving the api URL as /api/stats/(container-id)/(filter-id)
        /// <list type="bullet">  
        ///     <listheader>  
        ///         <term>Possible filters are</term>  
        ///     </listheader>  
        ///     <item>  
        ///         <term>eth0</term>
        ///         <description>Network statistics</description>
        ///     </item>
        ///     <item>  
        ///         <term>memory_stats</term>
        ///         <description>Memory usage statistics</description>
        ///     </item>
        ///     <item>  
        ///         <term>cpu_stats</term>
        ///         <description>CPU usage statistics</description>
        ///     </item>
        /// </list>  
        /// </summary>
        /// <param name="containerId">ContainerId whose stats are to be retrieved</param>
        /// <param name="filterName"></param>
        /// <returns></returns>
        /// <exception cref="Exception">Filter for a particular containerId are to be retrieved</exception>
        [HttpGet("{containerId=all}/{filterName=all}")]
        public IActionResult GetLog(string instance,
            string containerId,
            string filterName)
        {
            try
            {
                lock (SiteInstStatsLock)
                {
                    if (_siteInstanceStats == null) throw new Exception("Metering stats not available right now.");
                    if (containerId.Equals("all"))
                    {
                        return Ok(JsonConvert.SerializeObject(_siteInstanceStats));
                    }
                    else
                    {
                        var areContainerStatsAvailable =
                            _siteInstanceStats.appContainersOnThisInstance.ContainsKey(containerId);
                        if (areContainerStatsAvailable)
                        {
                            if (filterName.Equals("all"))
                            {
                                return Ok(JsonConvert.SerializeObject(
                                    _siteInstanceStats.appContainersOnThisInstance[containerId]));
                            }

                            switch (filterName)
                            {
                                case "eth0":
                                    return Ok(JsonConvert.SerializeObject(
                                        _siteInstanceStats.appContainersOnThisInstance[containerId].Eth0));
                                case "memory_stats":
                                    return Ok(JsonConvert.SerializeObject(
                                        _siteInstanceStats.appContainersOnThisInstance[containerId].MemoryStats));
                                case "precpu_stats":
                                    return Ok(JsonConvert.SerializeObject(
                                        _siteInstanceStats.appContainersOnThisInstance[containerId].PreviousCpuStats));
                                case "cpu_stats":
                                    return Ok(JsonConvert.SerializeObject(
                                        _siteInstanceStats.appContainersOnThisInstance[containerId].CurrentCpuStats));
                                default:
                                    return BadRequest("Stats for filter " + filterName + " for container " +
                                                      containerId + " not found.");
                            }
                        }
                        else
                        {
                            return BadRequest("Stats for container " + containerId + " not found.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    JsonConvert.SerializeObject(new ExceptionResponse(ex.Message, ex.StackTrace)));
            }
        }
    }
}