using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.DTOs.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ConfigurationController : ControllerBase
    {
        private readonly IConfigurationStore _configurationStore;
        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(
            IConfigurationStore configurationStore,
            ILogger<ConfigurationController> logger)
        {
            _configurationStore = configurationStore;
            _logger = logger;
        }

        /// <summary>
        /// Get current configuration
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<AppConfiguration>> GetConfiguration()
        {
            var config = await _configurationStore.LoadConfigurationAsync();
            if (config == null)
            {
                return Ok(new AppConfiguration()); // Return empty config if not exists
            }
            return Ok(config);
        }

        /// <summary>
        /// Save configuration
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> SaveConfiguration([FromBody] AppConfiguration config)
        {
            try
            {
                await _configurationStore.SaveConfigurationAsync(config);
                _logger.LogInformation("Configuration saved successfully");
                return Ok(new { message = "Configuration saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration");
                return StatusCode(500, new { error = "Failed to save configuration", message = ex.Message });
            }
        }

        /// <summary>
        /// Check if configuration exists
        /// </summary>
        [HttpGet("exists")]
        public ActionResult ConfigurationExists()
        {
            return Ok(new { exists = _configurationStore.ConfigurationExists() });
        }
    }
}
