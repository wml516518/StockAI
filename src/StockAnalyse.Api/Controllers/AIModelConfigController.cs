using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;

namespace StockAnalyse.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIModelConfigController : ControllerBase
    {
        private readonly StockDbContext _context;

        public AIModelConfigController(StockDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 获取所有AI模型配置
        /// </summary>
        [HttpGet]
        public IActionResult GetAllConfigs()
        {
            var configs = _context.AIModelConfigs
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .AsEnumerable()
                .Select(ToResponse)
                .ToList();
            return Ok(configs);
        }

        /// <summary>
        /// 根据ID获取AI模型配置
        /// </summary>
        [HttpGet("{id}")]
        public IActionResult GetConfig(int id)
        {
            var config = _context.AIModelConfigs.FirstOrDefault(c => c.Id == id);
            if (config == null)
            {
                return NotFound($"未找到ID为{id}的配置");
            }
            return Ok(ToResponse(config));
        }

        /// <summary>
        /// 创建新的AI模型配置
        /// </summary>
        [HttpPost]
        public IActionResult CreateConfig([FromBody] AIModelConfigRequest request)
        {
            if (!TryValidateRequest(request, requireApiKey: true, out var validationError))
            {
                return BadRequest(validationError);
            }

            var config = new AIModelConfig
            {
                Name = request.Name.Trim(),
                ApiKey = request.ApiKey!.Trim(),
                SubscribeEndpoint = request.SubscribeEndpoint.Trim(),
                ModelName = request.ModelName!.Trim(),
                IsActive = request.IsActive,
                IsDefault = request.IsDefault,
            };

            HandleMutexLogic(config.IsActive, config.IsDefault);

            _context.AIModelConfigs.Add(config);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetConfig), new { id = config.Id }, ToResponse(config));
        }

        /// <summary>
        /// 更新AI模型配置
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateConfig(int id, [FromBody] AIModelConfigRequest request)
        {
            if (!TryValidateRequest(request, requireApiKey: false, out var validationError))
            {
                return BadRequest(validationError);
            }

            var existingConfig = await _context.AIModelConfigs.FirstOrDefaultAsync(c => c.Id == id);
            if (existingConfig == null)
            {
                return NotFound($"未找到ID为{id}的配置");
            }

            if (string.IsNullOrWhiteSpace(existingConfig.ApiKey)
                && string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest("API Key不能为空");
            }

            HandleMutexLogic(request.IsActive, request.IsDefault, id);

            existingConfig.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                existingConfig.ApiKey = request.ApiKey.Trim();
            }
            existingConfig.SubscribeEndpoint = request.SubscribeEndpoint.Trim();
            existingConfig.ModelName = request.ModelName!.Trim();
            existingConfig.IsActive = request.IsActive;
            existingConfig.IsDefault = request.IsDefault;

            await _context.SaveChangesAsync();

            return Ok(ToResponse(existingConfig));
        }

        /// <summary>
        /// 删除AI模型配置
        /// </summary>
        [HttpDelete("{id}")]
        public IActionResult DeleteConfig(int id)
        {
            var config = _context.AIModelConfigs.FirstOrDefault(c => c.Id == id);
            if (config == null)
            {
                return NotFound($"未找到ID为{id}的配置");
            }

            _context.AIModelConfigs.Remove(config);
            _context.SaveChanges();

            return NoContent();
        }

        /// <summary>
        /// 测试AI模型连接。传入Id时优先使用服务端保存的API Key。
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            AIModelConfig? storedConfig = null;
            if (request.Id > 0)
            {
                storedConfig = await _context.AIModelConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.Id);
                if (storedConfig == null)
                {
                    return NotFound($"未找到ID为{request.Id}的配置");
                }
            }

            var apiKey = string.IsNullOrWhiteSpace(request.ApiKey)
                ? storedConfig?.ApiKey
                : request.ApiKey.Trim();
            var endpoint = string.IsNullOrWhiteSpace(request.SubscribeEndpoint)
                ? storedConfig?.SubscribeEndpoint
                : request.SubscribeEndpoint.Trim();
            var modelName = string.IsNullOrWhiteSpace(request.ModelName)
                ? storedConfig?.ModelName
                : request.ModelName.Trim();

            if (string.IsNullOrWhiteSpace(apiKey)
                || string.IsNullOrWhiteSpace(endpoint)
                || string.IsNullOrWhiteSpace(modelName))
            {
                return BadRequest("API Key、订阅端点和模型名称不能为空");
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest("订阅端点必须是有效的 HTTP 或 HTTPS 地址");
            }

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

                var requestData = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "你好"
                        }
                    },
                    stream = false
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync(endpointUri, content);

                if (response.IsSuccessStatusCode)
                {
                    return Ok("连接测试成功");
                }

                return BadRequest(
                    $"连接测试失败: 上游服务返回 {(int)response.StatusCode} {response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return BadRequest("连接测试失败: 无法连接到模型服务或请求超时");
            }
        }

        private static bool TryValidateRequest(
            AIModelConfigRequest request,
            bool requireApiKey,
            out string validationError)
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.SubscribeEndpoint)
                || string.IsNullOrWhiteSpace(request.ModelName))
            {
                validationError = "配置名称、订阅端点和模型名称不能为空";
                return false;
            }

            if (requireApiKey && string.IsNullOrWhiteSpace(request.ApiKey))
            {
                validationError = "API Key不能为空";
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        private static AIModelConfigResponse ToResponse(AIModelConfig config)
        {
            return new AIModelConfigResponse(
                config.Id,
                config.Name,
                config.SubscribeEndpoint,
                config.ModelName,
                config.IsActive,
                config.IsDefault,
                !string.IsNullOrWhiteSpace(config.ApiKey)
                && !string.Equals(config.ApiKey, "your-api-key-here", StringComparison.Ordinal));
        }

        /// <summary>
        /// 处理激活和默认配置的互斥逻辑
        /// </summary>
        private void HandleMutexLogic(bool isActive, bool isDefault, int excludeId = 0)
        {
            if (isActive)
            {
                var activeConfigs = _context.AIModelConfigs
                    .Where(c => c.IsActive && c.Id != excludeId)
                    .ToList();
                foreach (var activeConfig in activeConfigs)
                {
                    activeConfig.IsActive = false;
                }
            }

            if (isDefault)
            {
                var defaultConfigs = _context.AIModelConfigs
                    .Where(c => c.IsDefault && c.Id != excludeId)
                    .ToList();
                foreach (var defaultConfig in defaultConfigs)
                {
                    defaultConfig.IsDefault = false;
                }
            }
        }
    }
}
