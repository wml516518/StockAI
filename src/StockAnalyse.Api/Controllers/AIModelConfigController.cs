using Microsoft.AspNetCore.Mvc;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using System.Text;
using System.Text.Json;

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
            var configs = _context.AIModelConfigs.ToList();
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
            return Ok(config);
        }

        /// <summary>
        /// 创建新的AI模型配置
        /// </summary>
        [HttpPost]
        public IActionResult CreateConfig([FromBody] AIModelConfig config)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // 处理互斥逻辑
            HandleMutexLogic(config);

            _context.AIModelConfigs.Add(config);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetConfig), new { id = config.Id }, config);
        }

        /// <summary>
        /// 更新AI模型配置
        /// </summary>
        [HttpPut("{id}")]
        public IActionResult UpdateConfig(int id, [FromBody] AIModelConfig config)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var existingConfig = _context.AIModelConfigs.FirstOrDefault(c => c.Id == id);
            if (existingConfig == null)
            {
                return NotFound($"未找到ID为{id}的配置");
            }

            // 处理互斥逻辑
            HandleMutexLogic(config, id);

            existingConfig.Name = config.Name;
            existingConfig.ApiKey = config.ApiKey;
            existingConfig.SubscribeEndpoint = config.SubscribeEndpoint;
            existingConfig.ModelName = config.ModelName;
            existingConfig.IsActive = config.IsActive;
            existingConfig.IsDefault = config.IsDefault;

            _context.SaveChanges();

            return Ok(existingConfig);
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
        /// 用于测试连接的请求DTO
        /// </summary>
        public class TestConnectionRequest
        {
            public string ApiKey { get; set; } = string.Empty;
            public string SubscribeEndpoint { get; set; } = string.Empty;
            public string ModelName { get; set; } = string.Empty;
        }

        /// <summary>
        /// 测试AI模型连接
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            // 验证必需字段
            if (string.IsNullOrWhiteSpace(request.ApiKey) || 
                string.IsNullOrWhiteSpace(request.SubscribeEndpoint) || 
                string.IsNullOrWhiteSpace(request.ModelName))
            {
                return BadRequest("API Key、订阅端点和模型名称不能为空");
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {request.ApiKey}");

                // 使用标准的OpenAI聊天完成API格式
                var requestData = new
                {
                    model = request.ModelName,
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

                var response = await client.PostAsync(request.SubscribeEndpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    return Ok("连接测试成功");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest($"连接测试失败: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"连接测试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理激活和默认配置的互斥逻辑
        /// </summary>
        private void HandleMutexLogic(AIModelConfig config, int excludeId = 0)
        {
            // 如果设置了激活状态，取消其他配置的激活状态
            if (config.IsActive)
            {
                var activeConfigs = _context.AIModelConfigs.Where(c => c.IsActive && c.Id != excludeId).ToList();
                foreach (var activeConfig in activeConfigs)
                {
                    activeConfig.IsActive = false;
                }
            }

            // 如果设置了默认配置，取消其他配置的默认状态
            if (config.IsDefault)
            {
                var defaultConfigs = _context.AIModelConfigs.Where(c => c.IsDefault && c.Id != excludeId).ToList();
                foreach (var defaultConfig in defaultConfigs)
                {
                    defaultConfig.IsDefault = false;
                }
            }
        }
    }
}