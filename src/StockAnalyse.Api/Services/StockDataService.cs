using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class StockDataService : IStockDataService
{
    private readonly StockDbContext _context;
    private readonly ILogger<StockDataService> _logger;
    private readonly HttpClient _httpClient;

    public StockDataService(StockDbContext context, ILogger<StockDataService> logger, HttpClient httpClient)
    {
        _context = context;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// 安全地将字符串转换为decimal，处理 "-"、空字符串等情况
    /// </summary>
    private decimal SafeConvertToDecimal(object? value, decimal defaultValue = 0)
    {
        if (value == null)
            return defaultValue;
        
        string? strValue = value.ToString();
        if (string.IsNullOrWhiteSpace(strValue) || strValue == "-" || strValue == "--")
            return defaultValue;
        
        if (decimal.TryParse(strValue, out decimal result))
            return result;
        
        return defaultValue;
    }

    public async Task<Stock?> GetRealTimeQuoteAsync(string stockCode)
    {
        try
        {
            // 优先尝试从东方财富获取
            var stock = await FetchEastMoneyDataAsync(stockCode);
            
            // 如果东方财富失败，尝试新浪财经
            if (stock == null)
            {
                stock = await FetchSinaDataAsync(stockCode);
            }
            
            if (stock != null)
            {
                await SaveStockDataAsync(stock);
            }
            
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票行情失败: {StockCode}", stockCode);
            
            // 如果API失败，从数据库获取最新数据
            return await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        }
    }

    public async Task<Stock?> GetWatchlistRealTimeQuoteAsync(string stockCode)
    {
        try
        {
            // 优先尝试从东方财富获取
            var stock = await FetchEastMoneyDataAsync(stockCode);
            
            // 如果东方财富失败，尝试新浪财经
            if (stock == null)
            {
                stock = await FetchSinaDataAsync(stockCode);
            }
            
            // 注意：这里不保存到数据库，确保总是获取最新数据
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取自选股实时行情失败: {StockCode}", stockCode);
            return null;
        }
    }

    public async Task<List<Stock>> GetBatchQuotesAsync(IEnumerable<string> stockCodes)
    {
        var codes = stockCodes.ToList();
        var stocks = new List<Stock>();
        
        foreach (var code in codes)
        {
            var stock = await GetRealTimeQuoteAsync(code);
            if (stock != null)
            {
                stocks.Add(stock);
            }
        }
        
        return stocks;
    }

    public async Task SaveStockDataAsync(Stock stock)
    {
        var existing = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stock.Code);
        
        if (existing != null)
        {
            // 更新现有记录，包括股票名称和其他重要信息
            existing.Name = stock.Name;  // 添加了名称更新
            existing.Market = stock.Market;  // 添加了市场信息更新
            existing.CurrentPrice = stock.CurrentPrice;
            existing.OpenPrice = stock.OpenPrice;
            existing.ClosePrice = stock.ClosePrice;
            existing.HighPrice = stock.HighPrice;
            existing.LowPrice = stock.LowPrice;
            existing.Volume = stock.Volume;
            existing.Turnover = stock.Turnover;
            existing.ChangePercent = stock.ChangePercent;
            existing.ChangeAmount = stock.ChangeAmount;
            existing.TurnoverRate = stock.TurnoverRate;
            existing.PE = stock.PE;  // 添加了市盈率更新
            existing.PB = stock.PB;  // 添加了市净率更新
            existing.LastUpdate = DateTime.Now;
        }
        else
        {
            stock.LastUpdate = DateTime.Now;
            await _context.Stocks.AddAsync(stock);
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task<List<StockHistory>> GetDailyDataAsync(string stockCode, DateTime startDate, DateTime endDate)
    {
        return await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate >= startDate && h.TradeDate <= endDate)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();
    }

    public async Task<(decimal macd, decimal signal, decimal histogram)> CalculateMACDAsync(string stockCode)
    {
        // 获取最近的数据
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(100)
            .ToListAsync();
            
        if (histories.Count < 26)
        {
            return (0, 0, 0);
        }
        
        // 简化的MACD计算（实际应该使用标准的EMA算法）
        var closes = histories.Select(h => h.Close).Reverse().ToArray();
        
        // EMA12
        var ema12 = CalculateEMA(closes, 12);
        // EMA26
        var ema26 = CalculateEMA(closes, 26);
        
        var macd = ema12 - ema26;
        
        // 简化的Signal计算
        var signal = macd * 0.9m;
        var histogram = macd - signal;
        
        // 不再更新数据库中的MACD字段，因为这些字段已被删除
        // 只返回计算结果供调用方使用
        
        return (macd, signal, histogram);
    }

    public async Task<List<Stock>> GetRankingListAsync(string market, int top = 100)
    {
        return await _context.Stocks
            .Where(s => s.Market == market)
            .OrderByDescending(s => s.ChangePercent)
            .Take(top)
            .ToListAsync();
    }

    private Stock? ParseSinaData(string data, string stockCode)
    {
        try
        {
            // 新浪财经返回格式示例：var hq_str_sz000001="平安银行,19.82,19.82,19.70,..."
            // 需要正确解析返回的数据格式
            var match = System.Text.RegularExpressions.Regex.Match(data, @"\""(.*?)\""");
            if (!match.Success)
            {
                _logger.LogWarning("无法解析新浪财经返回数据: {Data}", data);
                return null;
            }
            
            var parts = match.Groups[1].Value.Split(',');
            if (parts.Length < 32)
            {
                _logger.LogWarning("新浪财经返回数据字段不足: {Data}", data);
                return null;
            }
            
            var name = parts[0]; // 股票名称在第一个位置
            var open = SafeConvertToDecimal(parts[1]);
            var prevClose = SafeConvertToDecimal(parts[2]);
            var current = SafeConvertToDecimal(parts[3]) / 100;
            var high = SafeConvertToDecimal(parts[4]);
            var low = SafeConvertToDecimal(parts[5]);
            var volume = SafeConvertToDecimal(parts[8]);
            var turnover = SafeConvertToDecimal(parts[9]);
            
            var changeAmount = current - prevClose;
            var changePercent = prevClose != 0 ? changeAmount / prevClose * 100 : 0;
            var turnoverRate = volume > 0 ? turnover / volume * 100 : 0;
            
            return new Stock
            {
                Code = stockCode,
                Name = name,
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                CurrentPrice = current,
                OpenPrice = open,
                ClosePrice = prevClose,
                HighPrice = high,
                LowPrice = low,
                Volume = volume,
                Turnover = turnover,
                ChangeAmount = changeAmount,
                ChangePercent = changePercent,
                TurnoverRate = turnoverRate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析股票数据失败: {Data}", data);
            return null;
        }
    }

    private decimal CalculateEMA(decimal[] prices, int period)
    {
        if (prices.Length == 0)
            return 0;
            
        decimal multiplier = 2m / (period + 1);
        decimal ema = prices[0];
        
        for (int i = 1; i < prices.Length; i++)
        {
            ema = (prices[i] * multiplier) + (ema * (1 - multiplier));
        }
        
        return ema;
    }
    
    /// <summary>
    /// 从东方财富获取股票数据
    /// </summary>
    private async Task<Stock?> FetchEastMoneyDataAsync(string stockCode)
    {
        try
        {
            // 判断市场：1=上交所, 0=深交所
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            
            // 补充PE(PETTM f162)与PB(f167)字段，避免PE/PB一直为0导致筛选结果为空
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&fields=f57,f58,f107,f137,f43,f46,f44,f45,f47,f48,f168,f60,f170,f116,f171,f117,f172,f169,f162,f167&fltt=2";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            
            if (data?.data == null)
            {
                return null;
            }
            
            var stockInfo = data.data;
            
            // 获取各个价格字段（使用安全转换方法，处理"-"等无效值）
            decimal currentPrice = SafeConvertToDecimal(stockInfo.f43);
            decimal openPrice = SafeConvertToDecimal(stockInfo.f46);
            decimal closePrice = SafeConvertToDecimal(stockInfo.f60); // 昨收价
            decimal highPrice = SafeConvertToDecimal(stockInfo.f44);
            decimal lowPrice = SafeConvertToDecimal(stockInfo.f45);
            
            // 价格回退逻辑：非交易时间使用昨收价
            if (currentPrice == 0.0m && closePrice > 0.0m)
                currentPrice = closePrice;
            
            if (openPrice == 0 && closePrice > 0)
                openPrice = closePrice;
            
            if (highPrice == 0) 
                highPrice = currentPrice;
            if (lowPrice == 0) 
                lowPrice = currentPrice;
            
            var stock = new Stock
            {
                Code = stockCode,
                Name = stockInfo.f58?.ToString() ?? "未知",
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                CurrentPrice = currentPrice,
                OpenPrice = openPrice,
                ClosePrice = closePrice,
                HighPrice = highPrice,
                LowPrice = lowPrice,
                Volume = SafeConvertToDecimal(stockInfo.f47),
                Turnover = SafeConvertToDecimal(stockInfo.f48),
                ChangeAmount = SafeConvertToDecimal(stockInfo.f169),
                ChangePercent = SafeConvertToDecimal(stockInfo.f170),
                TurnoverRate = SafeConvertToDecimal(stockInfo.f168),
                PE = SafeConvertToDecimal(stockInfo.f162) > 0 ? SafeConvertToDecimal(stockInfo.f162) : null,  // 市盈率
                PB = SafeConvertToDecimal(stockInfo.f167) > 0 ? SafeConvertToDecimal(stockInfo.f167) : null,  // 市净率
                LastUpdate = DateTime.Now
            };
            
            return stock;
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    /// <summary>
    /// 从新浪财经获取（备用）
    /// </summary>
    private async Task<Stock?> FetchSinaDataAsync(string stockCode)
    {
        try
        {
            // 根据股票代码确定市场前缀
            var marketPrefix = stockCode.StartsWith("6") ? "sh" : "sz";
            var url = $"http://hq.sinajs.cn/list={marketPrefix}{stockCode}";
            _logger.LogInformation("请求新浪财经接口: {Url}", url);
            
            // 设置请求头
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://finance.sina.com.cn");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.LogInformation("新浪财经返回数据: {Response}", response);
            
            var stock = ParseSinaData(response, stockCode);
            
            if (stock != null)
            {
                _logger.LogInformation("成功从新浪财经获取: {Code} {Name}", stock.Code, stock.Name);
            }
            else
            {
                _logger.LogWarning("解析新浪财经数据失败: {Response}", response);
            }
            
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新浪财经获取失败: {Code}", stockCode);
            return null;
        }
    }

    public async Task<int> FetchAndStoreDailyHistoryAsync(string stockCode, DateTime startDate, DateTime endDate)
    {
        int saved = 0;
        try
        {
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            var beg = startDate.ToString("yyyyMMdd");
            var end = endDate.ToString("yyyyMMdd");

            var url = $"http://push2his.eastmoney.com/api/qt/stock/kline/get?secid={secid}&fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56,f57&klt=101&fqt=1&beg={beg}&end={end}";
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");

            var response = await _httpClient.GetStringAsync(url);
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            if (data?.data?.klines == null)
            {
                _logger.LogWarning("东方财富日线数据为空: {Code}", stockCode);
                return 0;
            }

            foreach (var k in data.data.klines)
            {
                string line = k.ToString(); // "YYYY-MM-DD,open,close,high,low,volume,amount"
                var parts = line.Split(',');
                if (parts.Length < 7) continue;

                try
                {
                    var tradeDate = DateTime.Parse(parts[0]);
                    var open = SafeConvertToDecimal(parts[1]);
                    var close = SafeConvertToDecimal(parts[2]);
                    var high = SafeConvertToDecimal(parts[3]);
                    var low = SafeConvertToDecimal(parts[4]);
                    var volume = SafeConvertToDecimal(parts[5]);
                    var amount = SafeConvertToDecimal(parts[6]);

                    // 跳过无效数据（价格为0或负数）
                    if (open <= 0 || close <= 0 || high <= 0 || low <= 0)
                        continue;

                    var existing = await _context.StockHistories
                        .FirstOrDefaultAsync(h => h.StockCode == stockCode && h.TradeDate == tradeDate);

                    if (existing != null)
                    {
                        existing.Open = open;
                        existing.Close = close;
                        existing.High = high;
                        existing.Low = low;
                        existing.Volume = volume;
                        existing.Turnover = amount;
                    }
                    else
                    {
                        await _context.StockHistories.AddAsync(new StockHistory
                        {
                            StockCode = stockCode,
                            TradeDate = tradeDate,
                            Open = open,
                            Close = close,
                            High = high,
                            Low = low,
                            Volume = volume,
                            Turnover = amount
                        });
                    }
                    
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("解析日线数据失败: {Line}, 错误: {Error}", line, ex.Message);
                    continue;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("保存{Count}条 {Code} 日线历史", saved, stockCode);
            return saved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拉取并保存东方财富日线失败: {Code}", stockCode);
            return 0;
        }
    }

    /// <summary>
    /// 从腾讯财经获取股票列表（替代方案，数据更准确）
    /// </summary>
    public async Task<List<Stock>> FetchAllStocksFromTencentAsync(string? market = null, int maxCount = 2000)
    {
        var allStocks = new List<Stock>();
        
        try
        {
            // 如果是全部市场，需要允许获取更多股票（每个市场各 maxCount 个）
            int actualMaxCount = market == null ? maxCount * 2 : maxCount;
            
            _logger.LogInformation("开始从腾讯财经获取股票列表，市场: {Market}, 最大数量: {ActualMaxCount} (原始: {MaxCount})", 
                market ?? "全部", actualMaxCount, maxCount);
            
            var stockCodes = GenerateStockCodeList(market, actualMaxCount * 2);
            _logger.LogInformation("生成 {Count} 个股票代码", stockCodes.Count);
            
            int batchSize = 100;
            for (int i = 0; i < stockCodes.Count && allStocks.Count < actualMaxCount; i += batchSize)
            {
                var batch = stockCodes.Skip(i).Take(batchSize).ToList();
                var batchStocks = await FetchBatchFromTencentAsync(batch);
                
                foreach (var stock in batchStocks)
                {
                    // 放宽过滤条件：允许临时名称，价格可以稍微宽松（某些股票可能价格暂时为0但数据有效）
                    if (stock != null && !string.IsNullOrWhiteSpace(stock.Code) && 
                        !string.IsNullOrWhiteSpace(stock.Name))
                    {
                        // 如果价格无效，但有其他有效数据，仍然保留（可能在非交易时间）
                        if (stock.CurrentPrice <= 0 && stock.ClosePrice > 0)
                        {
                            stock.CurrentPrice = stock.ClosePrice; // 使用昨收价
                        }
                        
                        // 只有当完全没有价格信息时才跳过
                        if (stock.CurrentPrice > 0 || stock.ClosePrice > 0)
                        {
                            allStocks.Add(stock);
                            if (allStocks.Count >= actualMaxCount)
                                break;
                        }
                        else
                        {
                            _logger.LogDebug("跳过无价格信息的股票: {Code} {Name}", stock.Code, stock.Name);
                        }
                    }
                }
                
                await Task.Delay(300);
                
                if ((i + batchSize) % 500 == 0 || allStocks.Count % 500 == 0)
                {
                    _logger.LogInformation("已获取 {Count}/{Total} 只有效股票", allStocks.Count, actualMaxCount);
                }
            }
            
            _logger.LogInformation("从腾讯财经获取到 {Count} 只有效股票 (目标: {Target})", allStocks.Count, actualMaxCount);
            
            // 如果是全部市场，统计各市场的数量
            if (market == null)
            {
                var shCount = allStocks.Count(s => s.Market == "SH");
                var szCount = allStocks.Count(s => s.Market == "SZ");
                _logger.LogInformation("市场分布 - 上海: {SHCount}, 深圳: {SZCount}, 总计: {Total}", shCount, szCount, allStocks.Count);
            }
            return allStocks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从腾讯财经获取股票列表失败");
            return allStocks;
        }
    }

    /// <summary>
    /// 批量从腾讯财经获取股票数据
    /// </summary>
    private async Task<List<Stock>> FetchBatchFromTencentAsync(List<string> stockCodes)
    {
        var stocks = new List<Stock>();
        
        try
        {
            var codeList = stockCodes.Select(code =>
            {
                var prefix = code.StartsWith("6") ? "sh" : "sz";
                return $"{prefix}{code}";
            }).ToList();
            
            var url = $"http://qt.gtimg.cn/q={string.Join(",", codeList)}";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            // 使用GetByteArrayAsync然后手动解码，解决编码问题
            var responseBytes = await _httpClient.GetByteArrayAsync(url);
            // 腾讯财经返回的是GBK编码
            // .NET Core需要注册CodePages编码提供程序才能使用GBK
            // 确保编码提供程序已注册（如果未注册则注册，已注册则忽略）
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // 可能已经注册过，忽略异常
            }
            
            System.Text.Encoding gbkEncoding;
            try
            {
                gbkEncoding = System.Text.Encoding.GetEncoding("GBK");
            }
            catch (Exception ex)
            {
                // 如果GBK不可用，尝试GB2312
                try
                {
                    gbkEncoding = System.Text.Encoding.GetEncoding("GB2312");
                    _logger.LogDebug("使用GB2312编码替代GBK");
                }
                catch
                {
                    // 如果都不可用，使用UTF-8（可能会乱码，但不会崩溃）
                    gbkEncoding = System.Text.Encoding.UTF8;
                    _logger.LogWarning("无法使用GBK/GB2312编码，使用UTF-8可能会导致中文乱码。错误: {Error}", ex.Message);
                }
            }
            var response = gbkEncoding.GetString(responseBytes);
            
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                try
                {
                    // 腾讯财经返回格式可能是：
                    // 1. v_sh603901="1~股票名~603901~价格~...";
                    // 2. ~603901~12.81~... (没有变量名和引号)
                    
                    string dataLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(dataLine))
                        continue;
                    
                    string dataContent = "";
                    
                    // 尝试格式1：带变量名和引号的格式
                    var match = System.Text.RegularExpressions.Regex.Match(dataLine, @"v_\w+=""([^""]+)""");
                    if (match.Success)
                    {
                        dataContent = match.Groups[1].Value;
                    }
                    else
                    {
                        // 尝试格式2：直接以~开头的格式
                        if (dataLine.StartsWith("~"))
                        {
                            // 移除开头的~，然后解析
                            dataContent = dataLine.Substring(1);
                        }
                        else
                        {
                            // 尝试其他格式：可能没有引号
                            var match2 = System.Text.RegularExpressions.Regex.Match(dataLine, @"=\""?([^""]+)""?\s*;?\s*$");
                            if (match2.Success)
                            {
                                dataContent = match2.Groups[1].Value;
                            }
                            else
                            {
                                // 如果都不匹配，尝试直接用整行（去除变量名部分）
                                var index = dataLine.IndexOf('=');
                                if (index > 0)
                                {
                                    dataContent = dataLine.Substring(index + 1).Trim().Trim('"').Trim(';');
                                }
                                else
                                {
                                    continue; // 无法解析，跳过
                                }
                            }
                        }
                    }
                    
                    var parts = dataContent.Split('~');
                    // 降低最小字段要求，因为有些股票可能字段较少
                    if (parts.Length < 10) 
                    {
                        _logger.LogDebug("数据字段太少，跳过。字段数: {Count}, 内容: {Content}", parts.Length, dataContent.Substring(0, Math.Min(100, dataContent.Length)));
                        continue;
                    }
                    
                    // 根据格式调整索引
                    // 格式1：parts[0]="1", parts[1]=名称, parts[2]=代码, parts[3]=当前价, parts[4]=昨收, parts[5]=今开
                    // 格式2：parts[0]=代码, parts[1]=当前价, parts[2]=今开, parts[3]=昨收（注意：格式2没有名称字段）
                    // 格式3：可能还有其他变体，比如 parts[0] 不是"1"但包含名称
                    string code = "";
                    string name = "";
                    int priceIndexOffset = 0; // 价格字段的索引偏移量
                    
                    if (parts.Length > 2 && parts[0] == "1")
                    {
                        // 标准格式1：v_sh603901="1~股票名~603901~当前价~昨收~今开~..."
                        name = parts[1];
                        code = parts[2];
                        priceIndexOffset = 0; // 价格从索引3开始
                    }
                    else if (parts.Length > 2 && parts[0].Length == 6 && parts[0].All(char.IsDigit))
                    {
                        // 格式2：~603901~12.81~12.89~12.90~...
                        // parts[0]=代码, parts[1]=当前价, parts[2]=今开, parts[3]=昨收
                        code = parts[0];
                        priceIndexOffset = -2; // 价格索引需要调整，因为格式不同
                        name = "未知";
                    }
                    else if (parts.Length > 2 && parts[2].Length == 6 && parts[2].All(char.IsDigit))
                    {
                        // 格式3：可能是 parts[0]="0"或其他，parts[1]=名称, parts[2]=代码
                        // 尝试这种格式（常见于某些市场）
                        if (parts[1].Length > 0 && !parts[1].All(char.IsDigit) && parts[1].Length < 20)
                        {
                            name = parts[1];
                            code = parts[2];
                            priceIndexOffset = 0;
                        }
                        else
                        {
                            // 如果 parts[1] 不是名称，尝试作为格式2处理
                            code = parts[0].Length == 6 && parts[0].All(char.IsDigit) ? parts[0] : 
                                   (parts[2].Length == 6 && parts[2].All(char.IsDigit) ? parts[2] : "");
                            if (string.IsNullOrEmpty(code))
                            {
                                _logger.LogDebug("无法识别股票代码格式，跳过。内容: {Content}", dataContent.Substring(0, Math.Min(100, dataContent.Length)));
                                continue;
                            }
                            priceIndexOffset = -2;
                            name = "未知";
                        }
                    }
                    else
                    {
                        // 尝试查找6位数字作为股票代码
                        string? foundCode = null;
                        for (int i = 0; i < Math.Min(parts.Length, 10); i++)
                        {
                            if (parts[i].Length == 6 && parts[i].All(char.IsDigit))
                            {
                                foundCode = parts[i];
                                // 如果前一个字段可能是名称（非纯数字且长度合理）
                                if (i > 0 && !parts[i-1].All(char.IsDigit) && parts[i-1].Length > 0 && parts[i-1].Length < 20)
                                {
                                    name = parts[i-1];
                                    priceIndexOffset = 0;
                                }
                                else
                                {
                                    name = "未知";
                                    priceIndexOffset = -2;
                                }
                                break;
                            }
                        }
                        
                        if (foundCode == null)
                        {
                            _logger.LogDebug("无法找到有效的股票代码，跳过。内容: {Content}", dataContent.Substring(0, Math.Min(100, dataContent.Length)));
                            continue;
                        }
                        code = foundCode;
                    }
                    
                    if (string.IsNullOrWhiteSpace(code) || code == "N/A")
                        continue;
                    
                    // 根据格式获取价格字段
                    decimal currentPrice, openPrice, prevClose;
                    
                    if (priceIndexOffset == 0)
                    {
                        // 标准格式1
                        currentPrice = SafeConvertToDecimal(parts[3]);
                        prevClose = SafeConvertToDecimal(parts[4]);
                        openPrice = SafeConvertToDecimal(parts[5]);
                    }
                    else
                    {
                        // 格式2：~603901~当前价~今开~昨收~
                        currentPrice = SafeConvertToDecimal(parts[1]);
                        openPrice = SafeConvertToDecimal(parts[2]);
                        prevClose = SafeConvertToDecimal(parts[3]);
                    }
                    
                    // 其他字段的索引需要根据格式调整
                    // 标准格式1中：parts[6]=成交量, parts[37]=成交额, parts[33]=最高, parts[34]=最低
                    // 格式2中需要找到对应的字段位置（通常在这些索引附近）
                    
                    decimal volume, turnover, highPrice, lowPrice;
                    decimal changeAmount, changePercent;
                    
                    if (priceIndexOffset == 0)
                    {
                        // 标准格式1
                        volume = SafeConvertToDecimal(parts[6]);
                        turnover = parts.Length > 37 ? SafeConvertToDecimal(parts[37]) : 0;
                        highPrice = parts.Length > 33 ? SafeConvertToDecimal(parts[33]) : currentPrice;
                        lowPrice = parts.Length > 34 ? SafeConvertToDecimal(parts[34]) : currentPrice;
                        
                        changeAmount = currentPrice - prevClose;
                        changePercent = prevClose != 0 ? changeAmount / prevClose * 100 : 0;
                    }
                    else
                    {
                        // 格式2：需要从字段中找到对应的值
                        // 根据提供的格式：~603901~12.81~12.89~12.90~280684~130878~149806~...
                        // parts[4]可能是成交量，需要尝试不同索引
                        volume = parts.Length > 4 ? SafeConvertToDecimal(parts[4]) : 0;
                        turnover = parts.Length > 6 ? SafeConvertToDecimal(parts[6]) : 0;
                        
                        // 尝试找到最高价和最低价（通常在后面的字段）
                        highPrice = currentPrice;
                        lowPrice = currentPrice;
                        for (int i = 20; i < Math.Min(parts.Length, 40); i++)
                        {
                            var val = SafeConvertToDecimal(parts[i]);
                            if (val > highPrice && val > currentPrice * 0.5m && val < currentPrice * 1.5m)
                                highPrice = val;
                            if (val < lowPrice && val > 0 && val < currentPrice * 1.5m)
                                lowPrice = val;
                        }
                        
                        // 涨跌幅可能在特定位置，尝试查找
                        changeAmount = currentPrice - prevClose;
                        changePercent = prevClose != 0 ? changeAmount / prevClose * 100 : 0;
                        
                        // 如果parts中有明确的涨跌幅字段，使用它
                        // 通常在20-30之间的位置
                        for (int i = 20; i < Math.Min(parts.Length, 30); i++)
                        {
                            var val = SafeConvertToDecimal(parts[i]);
                            if (Math.Abs(val) < 20 && Math.Abs(val) > 0.001m) // 涨跌幅通常在-20到20之间
                            {
                                changePercent = val;
                                changeAmount = prevClose * val / 100;
                                break;
                            }
                        }
                    }
                    
                    // 换手率和PE/PB（这些字段在不同格式中位置可能不同）
                    decimal turnoverRate = 0m;
                    decimal pe = 0;
                    decimal pb = 0;
                    
                    // 尝试从常见位置获取
                    if (parts.Length > 38)
                    {
                        turnoverRate = SafeConvertToDecimal(parts[38]);
                    }
                    if (parts.Length > 39)
                    {
                        pe = SafeConvertToDecimal(parts[39]);
                    }
                    if (parts.Length > 46)
                    {
                        pb = SafeConvertToDecimal(parts[46]);
                    }
                    
                    // 如果名称是"未知"（格式2），尝试从数据库获取，或者使用临时名称
                    if (name == "未知")
                    {
                        // 尝试从数据库获取名称（如果有缓存）
                        try
                        {
                            var cachedStock = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == code);
                            if (cachedStock != null && !string.IsNullOrWhiteSpace(cachedStock.Name))
                            {
                                name = cachedStock.Name;
                            }
                            else
                            {
                                // 如果数据库中没有，使用临时名称（股票代码），至少让数据能保存下来
                                // 后续可以通过其他接口补充名称
                                name = $"股票{code}";
                                _logger.LogDebug("腾讯财经返回格式2，名称未知，使用临时名称: {Code}", code);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 如果数据库查询失败，使用临时名称
                            name = $"股票{code}";
                            _logger.LogDebug(ex, "从数据库获取股票名称失败，使用临时名称: {Code}", code);
                        }
                    }
                    
                    // 验证名称有效性（允许临时名称通过）
                    if (string.IsNullOrWhiteSpace(name) || name == "N/A" || name == "-")
                    {
                        // 如果名称仍然无效，使用临时名称
                        name = $"股票{code}";
                    }
                    
                    stocks.Add(new Stock
                    {
                        Code = code,
                        Name = name,
                        Market = code.StartsWith("6") ? "SH" : "SZ",
                        CurrentPrice = currentPrice,
                        OpenPrice = openPrice > 0 ? openPrice : prevClose,
                        ClosePrice = prevClose,
                        HighPrice = highPrice > 0 ? highPrice : currentPrice,
                        LowPrice = lowPrice > 0 ? lowPrice : currentPrice,
                        Volume = volume * 100,
                        Turnover = turnover,
                        ChangeAmount = changeAmount,
                        ChangePercent = changePercent,
                        TurnoverRate = turnoverRate,
                        PE = pe > 0 ? pe : null,
                        PB = pb > 0 ? pb : null,
                        LastUpdate = DateTime.Now
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("解析腾讯财经股票数据失败: {Error}", ex.Message);
                }
            }
            
            return stocks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("批量获取腾讯财经数据失败: {Error}", ex.Message);
            return stocks;
        }
    }
    
    /// <summary>
    /// 获取股票基本面信息（使用多个备用接口）
    /// </summary>
    public async Task<StockFundamentalInfo?> GetFundamentalInfoAsync(string stockCode)
    {
        Console.WriteLine($"[基本面数据] ============================================");
        Console.WriteLine($"[基本面数据] 开始获取股票 {stockCode} 的基本面信息");
        Console.WriteLine($"[基本面数据] ============================================");
        
        _logger.LogInformation("============================================");
        _logger.LogInformation("📊 [StockDataService] 开始获取股票 {StockCode} 的基本面信息", stockCode);
        _logger.LogInformation("============================================");
        
        // 尝试多个接口，按优先级顺序
        // 方案1: 使用Python服务（AKShare数据源）- 最推荐
        var result = await TryGetFundamentalInfoFromPythonServiceAsync(stockCode);
        if (result != null)
        {
            _logger.LogInformation("📊 [StockDataService] ✅ 从Python服务成功获取基本面信息");
            return result;
        }
        
            // 方案2: 使用东方财富F10详情接口（直接获取财务快照）
        Console.WriteLine($"[基本面数据] 方案2: 尝试从东方财富F10详情接口获取数据...");
        result = await TryGetFundamentalInfoFromF10DetailAsync(stockCode);
        if (result != null)
        {
            Console.WriteLine($"[基本面数据] ✅ 方案2成功：从F10详情接口获取到数据");
            _logger.LogInformation("📊 [StockDataService] ✅ 从F10详情接口成功获取基本面信息");
            return result;
        }
        
        // 方案3: 使用东方财富实时行情接口的扩展字段（从已知可用的接口获取）
        Console.WriteLine($"[基本面数据] 方案3: 尝试从实时行情接口获取数据...");
        result = await TryGetFundamentalInfoFromRealTimeAsync(stockCode);
        if (result != null)
        {
            Console.WriteLine($"[基本面数据] ✅ 方案3成功：从实时行情接口获取到数据");
            _logger.LogInformation("📊 [StockDataService] ✅ 从实时行情接口成功获取基本面信息");
            return result;
        }
        
        // 方案4: 尝试使用F10资产负债表接口
        Console.WriteLine($"[基本面数据] 方案4: 尝试从F10资产负债表接口获取数据...");
        result = await TryGetFundamentalInfoFromF10Async(stockCode);
        if (result != null)
        {
            Console.WriteLine($"[基本面数据] ✅ 方案4成功：从F10资产负债表接口获取到数据");
            _logger.LogInformation("📊 [StockDataService] ✅ 从F10资产负债表接口成功获取基本面信息");
            return result;
        }
        
        // 方案5: 使用财务指标接口（简化字段）
        Console.WriteLine($"[基本面数据] 方案5: 尝试从财务指标接口获取数据...");
        result = await TryGetFundamentalInfoFromFinanceAsync(stockCode);
        if (result != null)
        {
            Console.WriteLine($"[基本面数据] ✅ 方案5成功：从财务指标接口获取到数据");
            _logger.LogInformation("📊 [StockDataService] ✅ 从财务指标接口成功获取基本面信息");
            return result;
        }
        
        _logger.LogWarning("📊 [StockDataService] ❌ 所有接口均失败，返回基本估值信息");
        
        // 最后备用方案：至少返回PE/PB等基本信息
        var stock = await GetRealTimeQuoteAsync(stockCode);
        if (stock != null)
        {
            return new StockFundamentalInfo
            {
                StockCode = stockCode,
                StockName = stock.Name,
                PE = stock.PE,
                PB = stock.PB,
                LastUpdate = DateTime.Now
            };
        }
        
        return null;
    }
    
    /// <summary>
    /// 方案1: 从Python服务获取基本面信息（AKShare数据源，最推荐）
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromPythonServiceAsync(string stockCode)
    {
        try
        {
            // Python服务地址（默认localhost:5001，可通过配置修改）
            var pythonServiceUrl = Environment.GetEnvironmentVariable("PYTHON_DATA_SERVICE_URL") 
                ?? "http://localhost:5001";
            
            var url = $"{pythonServiceUrl}/api/stock/fundamental/{stockCode}";
            
            Console.WriteLine($"[基本面数据-方案1] 请求Python服务: {url}");
            _logger.LogInformation("📊 [StockDataService] 尝试Python服务: {Url}", url);
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Python服务可能需要更长时间
            
            // 使用GetAsync以便检查状态码
            var response = await _httpClient.GetAsync(url);
            
            // 如果返回404，说明数据未找到，不是服务不可用
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[基本面数据-方案1] ⚠️ Python服务(AKShare)无法获取股票 {stockCode} 的财务数据");
                Console.WriteLine($"[基本面数据-方案1] 💡 这是AKShare数据源的已知限制（某些创业板/科创板股票可能没有完整数据）");
                Console.WriteLine($"[基本面数据-方案1] 🔄 系统将自动尝试其他数据源（东方财富等）...");
                _logger.LogInformation("📊 [StockDataService] Python服务(AKShare)无法获取股票 {StockCode} 的数据，将尝试其他数据源", stockCode);
                return null; // 返回null，让系统尝试其他数据源
            }
            
            // 检查其他错误状态码
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[基本面数据-方案1] ⚠️ Python服务返回错误状态码: {(int)response.StatusCode} - {response.StatusCode}");
                Console.WriteLine($"[基本面数据-方案1] 错误详情: {errorContent}");
                _logger.LogWarning("📊 [StockDataService] Python服务返回错误状态码: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonData = Newtonsoft.Json.Linq.JObject.Parse(responseContent);
            
            if (jsonData["success"]?.ToString() == "True" && jsonData["data"] != null)
            {
                var data = jsonData["data"] as Newtonsoft.Json.Linq.JObject;
                if (data != null)
                {
                    // 同时获取股票基本信息（用于PE/PB）
                    var stock = await GetRealTimeQuoteAsync(stockCode);
                    
                    // 辅助方法：安全地从JObject获取decimal值
                    decimal? SafeGetDecimal(Newtonsoft.Json.Linq.JObject obj, string key)
                    {
                        var token = obj[key];
                        if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                            return null;
                        try
                        {
                            var value = token.ToObject<decimal?>();
                            return value;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    
                    var info = new StockFundamentalInfo
                    {
                        StockCode = stockCode,
                        StockName = data["stockName"]?.ToString() ?? stock?.Name ?? "未知",
                        ReportDate = data["reportDate"]?.ToString(),
                        ReportType = null,
                        
                        // 主要财务指标
                        TotalRevenue = SafeGetDecimal(data, "totalRevenue"),
                        NetProfit = SafeGetDecimal(data, "netProfit"),
                        
                        // 盈利能力
                        ROE = SafeGetDecimal(data, "roe"),
                        GrossProfitMargin = SafeGetDecimal(data, "grossProfitMargin"),
                        NetProfitMargin = SafeGetDecimal(data, "netProfitMargin"),
                        
                        // 成长性
                        RevenueGrowthRate = SafeGetDecimal(data, "revenueGrowthRate"),
                        ProfitGrowthRate = SafeGetDecimal(data, "profitGrowthRate"),
                        
                        // 偿债能力
                        AssetLiabilityRatio = SafeGetDecimal(data, "assetLiabilityRatio"),
                        CurrentRatio = SafeGetDecimal(data, "currentRatio"),
                        QuickRatio = SafeGetDecimal(data, "quickRatio"),
                        
                        // 运营能力
                        InventoryTurnover = SafeGetDecimal(data, "inventoryTurnover"),
                        AccountsReceivableTurnover = SafeGetDecimal(data, "accountsReceivableTurnover"),
                        
                        // 每股指标
                        EPS = SafeGetDecimal(data, "eps"),
                        BPS = SafeGetDecimal(data, "bps"),
                        CashFlowPerShare = null,
                        
                        // 估值指标（从实时行情获取，如果Python服务没有提供）
                        PE = stock?.PE,
                        PB = stock?.PB,
                        
                        LastUpdate = DateTime.Now
                    };
                    
                    Console.WriteLine($"[基本面数据-方案1] ✅ 从Python服务(AKShare)获取成功！");
                    Console.WriteLine($"[基本面数据-方案1]   数据完整性: 营收={info.TotalRevenue.HasValue}, 净利润={info.NetProfit.HasValue}, ROE={info.ROE.HasValue}, EPS={info.EPS.HasValue}");
                    _logger.LogInformation("📊 [StockDataService] ✅ 从Python服务(AKShare)获取成功 - 营收: {Revenue}万元, 净利润: {Profit}万元, ROE: {ROE}%, EPS: {EPS}元", 
                        info.TotalRevenue?.ToString("F2") ?? "N/A", 
                        info.NetProfit?.ToString("F2") ?? "N/A", 
                        info.ROE?.ToString("F2") ?? "N/A",
                        info.EPS?.ToString("F3") ?? "N/A");
                    
                    return info;
                }
            }
            
            return null;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            // 检查是否是404错误（数据未找到）
            if (ex.Message.Contains("404") || ex.Message.Contains("NOT FOUND"))
            {
                Console.WriteLine($"[基本面数据-方案1] ⚠️ Python服务返回404 - 股票代码 {stockCode} 的数据未找到");
                Console.WriteLine($"[基本面数据-方案1] 💡 提示: AKShare可能无法获取该股票的数据，将尝试其他数据源");
                _logger.LogDebug(ex, "📊 [StockDataService] Python服务返回404 - 股票代码 {StockCode} 的数据未找到", stockCode);
            }
            else
            {
                // Python服务可能未启动，这是正常的
                Console.WriteLine($"[基本面数据-方案1] ⚠️ Python服务未启动或不可用: {ex.Message}");
                _logger.LogDebug(ex, "📊 [StockDataService] Python服务不可用（可能未启动）");
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案1] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] Python服务调用失败");
            return null;
        }
    }
    
    /// <summary>
    /// 方案2: 从实时行情接口获取基本面信息（已知可用的接口）
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromF10DetailAsync(string stockCode)
    {
        try
        {
            // 直接使用已验证可用的实时行情接口，至少能获取PE/PB等基本信息
            var stock = await GetRealTimeQuoteAsync(stockCode);
            if (stock != null)
            {
                var info = new StockFundamentalInfo
                {
                    StockCode = stockCode,
                    StockName = stock.Name,
                    PE = stock.PE,
                    PB = stock.PB,
                    LastUpdate = DateTime.Now
                };
                
                Console.WriteLine($"[基本面数据-方案1] ✅ 从实时行情接口获取基本信息成功");
                _logger.LogInformation("📊 [StockDataService] 从实时行情接口获取PE={PE}, PB={PB}", stock.PE, stock.PB);
                return info;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案1] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] 实时行情接口失败");
            return null;
        }
    }
    
    /// <summary>
    /// 方案2: 从实时行情接口获取扩展的财务字段
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromRealTimeAsync(string stockCode)
    {
        try
        {
            // 判断市场：1=上交所, 0=深交所
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            
            // 使用扩展字段的实时行情接口（包含更多财务指标）
            // f10: 总市值, f12: 总股本, f13: 流通股本, f15: 最高价, f16: 最低价
            // f18: 昨收, f20: 总市值, f21: 流通市值, f23: 换手率, f24: 量比
            // f25: 市盈率, f26: 市净率, f37: 涨跌幅, f38: 涨跌额
            // f39: 成交额, f40: 成交量, f45: 最高, f46: 最低, f47: 今开, f48: 昨收
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&fields=f57,f58,f107,f137,f43,f46,f44,f45,f47,f48,f168,f60,f170,f116,f171,f117,f172,f169,f162,f167,f10,f12,f13,f20,f21,f25,f26&fltt=2";
            
            Console.WriteLine($"[基本面数据-方案2] 请求实时行情扩展接口");
            _logger.LogInformation("📊 [StockDataService] 尝试实时行情扩展接口");
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            
            if (data?.data != null)
            {
                var stockInfo = data.data;
                var stock = await GetRealTimeQuoteAsync(stockCode);
                
                var info = new StockFundamentalInfo
                {
                    StockCode = stockCode,
                    StockName = stockInfo.f58?.ToString() ?? stock?.Name ?? "未知",
                    PE = SafeConvertToDecimal(stockInfo.f162) > 0 ? SafeConvertToDecimal(stockInfo.f162) : null,
                    PB = SafeConvertToDecimal(stockInfo.f167) > 0 ? SafeConvertToDecimal(stockInfo.f167) : null,
                    LastUpdate = DateTime.Now
                };
                
                Console.WriteLine($"[基本面数据-方案2] ✅ 从实时行情接口获取成功");
                return info;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案2] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] 实时行情接口失败");
            return null;
        }
    }
    
    /// <summary>
    /// 方案3: 从F10资产负债表接口获取（保留原方法）
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromF10Async(string stockCode)
    {
        try
        {
            // 判断市场：1=上交所, 0=深交所
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            
            // 使用F10接口获取财务指标（更稳定的接口）
            var url = $"https://datacenter-web.eastmoney.com/api/data/v1/get?reportName=RPT_F10_FN_BALANCE&columns=SECURITY_CODE,SECURITY_NAME_ABBR,REPORT_DATE,REPORT_TYPE,TOTAL_OPERATE_INCOME,NET_PROFIT,ROE,GROSS_PROFIT_RATE,NET_PROFIT_RATE,REVENUE_YOY_RATE,PROFIT_YOY_RATE,ASSET_LIAB_RATIO,CURRENT_RATIO,QUICK_RATIO,EPS,BPS&filter=(SECURITY_CODE=%22{stockCode}%22)&pageNumber=1&pageSize=1&sortTypes=-1&sortColumns=REPORT_DATE";
            
            Console.WriteLine($"[基本面数据-方案1] 请求F10接口: {url}");
            _logger.LogInformation("📊 [StockDataService] 尝试F10接口: {Url}", url);
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://data.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            Console.WriteLine($"[基本面数据] API响应长度: {response.Length} 字符");
            Console.WriteLine($"[基本面数据] API响应内容: {response}");
            _logger.LogInformation("📊 [StockDataService] API响应长度: {Length} 字符", response.Length);
            _logger.LogInformation("📊 [StockDataService] API响应内容: {Response}", response);
            
            // 先尝试解析为JObject，以便更好地处理
            Newtonsoft.Json.Linq.JObject? jsonData = null;
            try
            {
                jsonData = Newtonsoft.Json.Linq.JObject.Parse(response);
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"[基本面数据] ❌ JSON解析失败: {parseEx.Message}");
                Console.WriteLine($"[基本面数据] 响应内容: {response}");
                _logger.LogError(parseEx, "📊 [StockDataService] JSON解析失败");
                return null;
            }
            
            // 检查API返回的数据结构
            if (jsonData == null)
            {
                Console.WriteLine($"[基本面数据] ❌ JSON解析结果为null");
                return null;
            }
            
            // 打印JSON结构以便调试
            Console.WriteLine($"[基本面数据] JSON根节点Keys: {string.Join(", ", jsonData.Properties().Select(p => p.Name))}");
            
            // 检查是否有错误信息
            if (jsonData["code"] != null)
            {
                var code = jsonData["code"].ToString();
                Console.WriteLine($"[基本面数据] API返回code: {code}");
                if (code != "0" && code != "200")
                {
                    var message = jsonData["message"]?.ToString() ?? "未知错误";
                    Console.WriteLine($"[基本面数据] ❌ API返回错误: code={code}, message={message}");
                    _logger.LogWarning("📊 [StockDataService] API返回错误: code={Code}, message={Message}", code, message);
                    return null;
                }
            }
            
            // 尝试不同的数据结构路径
            Newtonsoft.Json.Linq.JArray? dataArray = null;
            
            // 路径1: result.data
            if (jsonData["result"]?["data"] != null)
            {
                if (jsonData["result"]["data"] is Newtonsoft.Json.Linq.JArray array1)
                {
                    dataArray = array1;
                    Console.WriteLine($"[基本面数据] ✅ 找到数据路径: result.data (数组类型)");
                }
                else if (jsonData["result"]["data"] is Newtonsoft.Json.Linq.JObject)
                {
                    Console.WriteLine($"[基本面数据] ⚠️ result.data 是对象类型，尝试转换为数组");
                    // 可能是单个对象，需要转换为数组
                    dataArray = new Newtonsoft.Json.Linq.JArray { jsonData["result"]["data"] };
                }
            }
            
            // 路径2: data
            if (dataArray == null && jsonData["data"] != null)
            {
                if (jsonData["data"] is Newtonsoft.Json.Linq.JArray array2)
                {
                    dataArray = array2;
                    Console.WriteLine($"[基本面数据] ✅ 找到数据路径: data (数组类型)");
                }
            }
            
            // 路径3: result (直接是数组)
            if (dataArray == null && jsonData["result"] != null)
            {
                if (jsonData["result"] is Newtonsoft.Json.Linq.JArray array3)
                {
                    dataArray = array3;
                    Console.WriteLine($"[基本面数据] ✅ 找到数据路径: result (数组类型)");
                }
            }
            
            // 路径4: 尝试从result.records获取（某些API可能使用records）
            if (dataArray == null && jsonData["result"]?["records"] != null)
            {
                if (jsonData["result"]["records"] is Newtonsoft.Json.Linq.JArray array4)
                {
                    dataArray = array4;
                    Console.WriteLine($"[基本面数据] ✅ 找到数据路径: result.records (数组类型)");
                }
            }
            
            if (dataArray == null || dataArray.Count == 0)
            {
                Console.WriteLine($"[基本面数据] ❌ 未找到有效的财务数据数组");
                Console.WriteLine($"[基本面数据] JSON结构: {jsonData.ToString(Newtonsoft.Json.Formatting.Indented)}");
                _logger.LogWarning("📊 [StockDataService] ❌ 未找到股票 {Code} 的财务数据（未找到有效数组）", stockCode);
                
                // 如果API返回了错误，尝试使用备用方案：从实时行情获取基本信息
                Console.WriteLine($"[基本面数据] ⚠️ 尝试使用备用方案：从实时行情获取基本信息...");
                var fallbackStock = await GetRealTimeQuoteAsync(stockCode);
                if (fallbackStock != null)
                {
                    Console.WriteLine($"[基本面数据] ⚠️ 已从实时行情获取基本信息，但无法获取详细财务数据");
                }
                
                return null;
            }
            
            int dataCount = dataArray.Count;
            Console.WriteLine($"[基本面数据] ✅ 成功获取到财务数据，记录数: {dataCount}");
            _logger.LogInformation("📊 [StockDataService] ✅ 成功获取到财务数据，记录数: {Count}", dataCount);
            
            var financeData = dataArray[0] as Newtonsoft.Json.Linq.JObject;
            if (financeData == null)
            {
                Console.WriteLine($"[基本面数据] ❌ 无法将第一条数据转换为JObject");
                return null;
            }
            
            Console.WriteLine($"[基本面数据] 解析财务数据:");
            
            // 打印所有可用的字段名，便于调试
            var availableFields = financeData.Properties().Select(p => p.Name).ToList();
            Console.WriteLine($"[基本面数据] 可用字段: {string.Join(", ", availableFields)}");
            
            Console.WriteLine($"[基本面数据]   股票名称: {financeData["SECURITY_NAME_ABBR"]?.ToString() ?? "未知"}");
            
            // 尝试多种可能的日期和类型字段名
            string? reportDate = financeData["REPORT_DATE"]?.ToString() 
                ?? financeData["UPDATE_DATE"]?.ToString() 
                ?? financeData["DATE_TYPE_NAME"]?.ToString()
                ?? financeData["REPORTING_PERIOD"]?.ToString()
                ?? financeData["NOTICE_DATE"]?.ToString();
            
            string? reportType = financeData["REPORT_TYPE_NAME"]?.ToString()
                ?? financeData["DATE_TYPE_NAME"]?.ToString()
                ?? financeData["TYPE"]?.ToString()
                ?? financeData["REPORT_TYPE"]?.ToString();
            
            Console.WriteLine($"[基本面数据]   报告期: {reportDate ?? "未知"}");
            Console.WriteLine($"[基本面数据]   报告类型: {reportType ?? "未知"}");
            
            // 同时获取股票基本信息（用于获取PE、PB等）
            Console.WriteLine($"[基本面数据] 同时获取实时行情数据以补充PE/PB等信息...");
            var stock = await GetRealTimeQuoteAsync(stockCode);
            
            var info = new StockFundamentalInfo
            {
                StockCode = stockCode,
                StockName = financeData["SECURITY_NAME_ABBR"]?.ToString() ?? stock?.Name ?? "未知",
                ReportDate = reportDate,
                ReportType = reportType,
                
                // 主要财务指标（单位：万元，需要转换为万元）
                TotalRevenue = SafeConvertToDecimal(financeData["TOTAL_OPERATE_INCOME"]) / 10000,
                // 修复：使用NET_PROFIT替代不存在的NET_PROFIT_AFTER_DED_NRPLP，如果NET_PROFIT不存在则尝试其他字段
                NetProfit = (financeData["NET_PROFIT"] != null && financeData["NET_PROFIT"].ToString() != "")
                    ? SafeConvertToDecimal(financeData["NET_PROFIT"]) / 10000
                    : ((financeData["NET_PROFIT_AFTER_DED"] != null && financeData["NET_PROFIT_AFTER_DED"].ToString() != "")
                        ? SafeConvertToDecimal(financeData["NET_PROFIT_AFTER_DED"]) / 10000
                        : ((financeData["NET_PROFIT_ATTRIBUTABLE"] != null && financeData["NET_PROFIT_ATTRIBUTABLE"].ToString() != "")
                            ? SafeConvertToDecimal(financeData["NET_PROFIT_ATTRIBUTABLE"]) / 10000
                            : null)),
                
                // 盈利能力（%）
                ROE = SafeConvertToDecimal(financeData["ROE"]),
                GrossProfitMargin = SafeConvertToDecimal(financeData["GROSS_PROFIT_RATE"]),
                NetProfitMargin = SafeConvertToDecimal(financeData["NET_PROFIT_RATE"]),
                
                // 成长性（%）- 尝试多种可能的字段名
                RevenueGrowthRate = SafeConvertToDecimal(financeData["REVENUE_YOY_RATE"]) != 0 
                    ? SafeConvertToDecimal(financeData["REVENUE_YOY_RATE"])
                    : SafeConvertToDecimal(financeData["YOYSTOTALOPERATEINCOME"]),
                ProfitGrowthRate = SafeConvertToDecimal(financeData["PROFIT_YOY_RATE"]) != 0
                    ? SafeConvertToDecimal(financeData["PROFIT_YOY_RATE"])
                    : SafeConvertToDecimal(financeData["YOYSNETPROFIT"]),
                
                // 偿债能力
                AssetLiabilityRatio = SafeConvertToDecimal(financeData["ASSET_LIAB_RATIO"]),
                CurrentRatio = SafeConvertToDecimal(financeData["CURRENT_RATIO"]),
                QuickRatio = SafeConvertToDecimal(financeData["QUICK_RATIO"]),
                
                // 运营能力（可选字段）
                InventoryTurnover = financeData["INVENTORY_TURNOVER"] != null ? SafeConvertToDecimal(financeData["INVENTORY_TURNOVER"]) : null,
                AccountsReceivableTurnover = financeData["ACCOUNTS_RECEIVABLE_TURNOVER"] != null ? SafeConvertToDecimal(financeData["ACCOUNTS_RECEIVABLE_TURNOVER"]) : null,
                
                // 每股指标
                EPS = SafeConvertToDecimal(financeData["EPS"]),
                BPS = SafeConvertToDecimal(financeData["BPS"]),
                CashFlowPerShare = financeData["CASH_FLOW_PER_SHARE"] != null ? SafeConvertToDecimal(financeData["CASH_FLOW_PER_SHARE"]) : null,
                
                // 估值指标（从实时行情获取）
                PE = stock?.PE,
                PB = stock?.PB,
                
                LastUpdate = DateTime.Now
            };
            
            Console.WriteLine($"[基本面数据-方案1] ✅ 基本面信息解析完成");
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案1] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] F10接口失败");
            return null;
        }
    }
    
    /// <summary>
    /// 方案2: 从财务指标接口获取（简化字段版本）
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromFinanceAsync(string stockCode)
    {
        try
        {
            // 使用更简单的财务指标接口
            var url = $"https://datacenter-web.eastmoney.com/api/data/v1/get?reportName=RPT_LICO_FN_CPD&columns=SECURITY_CODE,SECURITY_NAME_ABBR,UPDATE_DATE,TOTAL_OPERATE_INCOME,NET_PROFIT,ROE,GROSS_PROFIT_RATE,NET_PROFIT_RATE,REVENUE_YOY_RATE,PROFIT_YOY_RATE,EPS,BPS&filter=(SECURITY_CODE=%22{stockCode}%22)&pageNumber=1&pageSize=1&sortTypes=-1&sortColumns=UPDATE_DATE";
            
            Console.WriteLine($"[基本面数据-方案2] 请求财务指标接口: {url}");
            _logger.LogInformation("📊 [StockDataService] 尝试财务指标接口: {Url}", url);
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://data.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            
            var jsonData = Newtonsoft.Json.Linq.JObject.Parse(response);
            
            // 检查错误
            if (jsonData["code"] != null && jsonData["code"].ToString() != "0" && jsonData["code"].ToString() != "200")
            {
                _logger.LogWarning("📊 [StockDataService] 财务指标接口返回错误: {Message}", jsonData["message"]?.ToString());
                return null;
            }
            
            // 获取数据数组
            var dataArray = jsonData["result"]?["data"] as Newtonsoft.Json.Linq.JArray
                ?? jsonData["data"] as Newtonsoft.Json.Linq.JArray
                ?? jsonData["result"] as Newtonsoft.Json.Linq.JArray;
            
            if (dataArray == null || dataArray.Count == 0)
                return null;
            
            var financeData = dataArray[0] as Newtonsoft.Json.Linq.JObject;
            if (financeData == null)
                return null;
            
            var stock = await GetRealTimeQuoteAsync(stockCode);
            
            var info = ParseFundamentalInfo(financeData, stockCode, stock);
            Console.WriteLine($"[基本面数据-方案2] ✅ 解析完成");
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案2] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] 财务指标接口失败");
            return null;
        }
    }
    
    /// <summary>
    /// 方案3: 从旧接口获取（兼容性）
    /// </summary>
    private async Task<StockFundamentalInfo?> TryGetFundamentalInfoFromOldApiAsync(string stockCode)
    {
        try
        {
            // 使用旧的接口（原接口，但字段已修复）
            var url = $"https://datacenter-web.eastmoney.com/api/data/v1/get?reportName=RPT_LICO_FN_CPD&columns=SECURITY_CODE,SECURITY_NAME_ABBR,NOTICE_DATE,UPDATE_DATE,TOTAL_OPERATE_INCOME,NET_PROFIT,ROE,GROSS_PROFIT_RATE,NET_PROFIT_RATE,REVENUE_YOY_RATE,PROFIT_YOY_RATE,ASSET_LIAB_RATIO,CURRENT_RATIO,QUICK_RATIO,INVENTORY_TURNOVER,ACCOUNTS_RECEIVABLE_TURNOVER,EPS,BPS,CASH_FLOW_PER_SHARE&filter=(SECURITY_CODE=%22{stockCode}%22)&pageNumber=1&pageSize=1&sortTypes=-1&sortColumns=UPDATE_DATE";
            
            Console.WriteLine($"[基本面数据-方案3] 请求旧接口: {url}");
            _logger.LogInformation("📊 [StockDataService] 尝试旧接口: {Url}", url);
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://data.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            
            var jsonData = Newtonsoft.Json.Linq.JObject.Parse(response);
            
            // 检查错误
            if (jsonData["code"] != null && jsonData["code"].ToString() != "0" && jsonData["code"].ToString() != "200")
            {
                _logger.LogWarning("📊 [StockDataService] 旧接口返回错误: {Message}", jsonData["message"]?.ToString());
                return null;
            }
            
            // 获取数据数组
            var dataArray = jsonData["result"]?["data"] as Newtonsoft.Json.Linq.JArray
                ?? jsonData["data"] as Newtonsoft.Json.Linq.JArray
                ?? jsonData["result"] as Newtonsoft.Json.Linq.JArray;
            
            if (dataArray == null || dataArray.Count == 0)
                return null;
            
            var financeData = dataArray[0] as Newtonsoft.Json.Linq.JObject;
            if (financeData == null)
                return null;
            
            var stock = await GetRealTimeQuoteAsync(stockCode);
            
            var info = ParseFundamentalInfo(financeData, stockCode, stock);
            Console.WriteLine($"[基本面数据-方案3] ✅ 解析完成");
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[基本面数据-方案3] ❌ 失败: {ex.Message}");
            _logger.LogWarning(ex, "📊 [StockDataService] 旧接口失败");
            return null;
        }
    }
    
    /// <summary>
    /// 解析财务数据为StockFundamentalInfo（通用方法）
    /// </summary>
    private StockFundamentalInfo ParseFundamentalInfo(Newtonsoft.Json.Linq.JObject financeData, string stockCode, Stock? stock)
    {
        // 尝试多种可能的日期和类型字段名
        string? reportDate = financeData["REPORT_DATE"]?.ToString() 
            ?? financeData["UPDATE_DATE"]?.ToString() 
            ?? financeData["DATE_TYPE_NAME"]?.ToString()
            ?? financeData["REPORTING_PERIOD"]?.ToString()
            ?? financeData["NOTICE_DATE"]?.ToString();
        
        string? reportType = financeData["REPORT_TYPE_NAME"]?.ToString()
            ?? financeData["DATE_TYPE_NAME"]?.ToString()
            ?? financeData["TYPE"]?.ToString()
            ?? financeData["REPORT_TYPE"]?.ToString();
        
        var info = new StockFundamentalInfo
        {
            StockCode = stockCode,
            StockName = financeData["SECURITY_NAME_ABBR"]?.ToString() ?? stock?.Name ?? "未知",
            ReportDate = reportDate,
            ReportType = reportType,
            
            // 主要财务指标（单位：万元）
            TotalRevenue = SafeConvertToDecimal(financeData["TOTAL_OPERATE_INCOME"]) / 10000,
            NetProfit = (financeData["NET_PROFIT"] != null && financeData["NET_PROFIT"].ToString() != "")
                ? SafeConvertToDecimal(financeData["NET_PROFIT"]) / 10000
                : ((financeData["NET_PROFIT_AFTER_DED"] != null && financeData["NET_PROFIT_AFTER_DED"].ToString() != "")
                    ? SafeConvertToDecimal(financeData["NET_PROFIT_AFTER_DED"]) / 10000
                    : ((financeData["NET_PROFIT_ATTRIBUTABLE"] != null && financeData["NET_PROFIT_ATTRIBUTABLE"].ToString() != "")
                        ? SafeConvertToDecimal(financeData["NET_PROFIT_ATTRIBUTABLE"]) / 10000
                        : null)),
            
            // 盈利能力（%）
            ROE = SafeConvertToDecimal(financeData["ROE"]),
            GrossProfitMargin = SafeConvertToDecimal(financeData["GROSS_PROFIT_RATE"]),
            NetProfitMargin = SafeConvertToDecimal(financeData["NET_PROFIT_RATE"]),
            
            // 成长性（%）
            RevenueGrowthRate = SafeConvertToDecimal(financeData["REVENUE_YOY_RATE"]) != 0 
                ? SafeConvertToDecimal(financeData["REVENUE_YOY_RATE"])
                : SafeConvertToDecimal(financeData["YOYSTOTALOPERATEINCOME"]),
            ProfitGrowthRate = SafeConvertToDecimal(financeData["PROFIT_YOY_RATE"]) != 0
                ? SafeConvertToDecimal(financeData["PROFIT_YOY_RATE"])
                : SafeConvertToDecimal(financeData["YOYSNETPROFIT"]),
            
            // 偿债能力（可选字段）
            AssetLiabilityRatio = financeData["ASSET_LIAB_RATIO"] != null ? SafeConvertToDecimal(financeData["ASSET_LIAB_RATIO"]) : null,
            CurrentRatio = financeData["CURRENT_RATIO"] != null ? SafeConvertToDecimal(financeData["CURRENT_RATIO"]) : null,
            QuickRatio = financeData["QUICK_RATIO"] != null ? SafeConvertToDecimal(financeData["QUICK_RATIO"]) : null,
            
            // 运营能力（可选字段）
            InventoryTurnover = financeData["INVENTORY_TURNOVER"] != null ? SafeConvertToDecimal(financeData["INVENTORY_TURNOVER"]) : null,
            AccountsReceivableTurnover = financeData["ACCOUNTS_RECEIVABLE_TURNOVER"] != null ? SafeConvertToDecimal(financeData["ACCOUNTS_RECEIVABLE_TURNOVER"]) : null,
            
            // 每股指标
            EPS = SafeConvertToDecimal(financeData["EPS"]),
            BPS = SafeConvertToDecimal(financeData["BPS"]),
            CashFlowPerShare = financeData["CASH_FLOW_PER_SHARE"] != null ? SafeConvertToDecimal(financeData["CASH_FLOW_PER_SHARE"]) : null,
            
            // 估值指标（从实时行情获取）
            PE = stock?.PE,
            PB = stock?.PB,
            
            LastUpdate = DateTime.Now
        };
        
        _logger.LogInformation("📊 [StockDataService] ✅ 成功解析基本面信息 - 营收: {Revenue}万元, 净利润: {Profit}万元, ROE: {ROE}%", 
            info.TotalRevenue?.ToString("F2") ?? "N/A", 
            info.NetProfit?.ToString("F2") ?? "N/A", 
            info.ROE?.ToString("F2") ?? "N/A");
        
        return info;
    }

    /// <summary>
    /// 生成股票代码列表
    /// </summary>
    private List<string> GenerateStockCodeList(string? market, int maxCount)
    {
        var codes = new List<string>();
        
        // 如果是全部市场，平均分配数量，确保两个市场都有股票
        // maxCount 是最终要获取的有效股票数量，生成代码时要生成更多（因为很多代码可能无效）
        int targetCodes = maxCount * 2; // 生成更多的代码以确保有足够的有效股票
        
        if (market == null)
        {
            // 全部市场：交替生成两个市场的代码，确保两个市场都能被处理
            int shTarget = targetCodes / 2; // 每个市场各分配一半
            int szTarget = targetCodes - shTarget;
            
            // 生成上海市场代码列表（先收集）
            var shCodes = new List<string>();
            for (int i = 600000; i <= 603999 && shCodes.Count < shTarget; i++)
                shCodes.Add(i.ToString());
            for (int i = 688000; i <= 689999 && shCodes.Count < shTarget; i++)
                shCodes.Add(i.ToString());
            
            // 生成深圳市场代码列表（先收集）
            var szCodes = new List<string>();
            for (int i = 1; i <= 2999 && szCodes.Count < szTarget; i++)
                szCodes.Add(i.ToString("D6"));
            for (int i = 300000; i <= 300999 && szCodes.Count < szTarget; i++)
                szCodes.Add(i.ToString());
            
            // 交替合并两个市场的代码，确保两个市场都能被处理
            int maxLen = Math.Max(shCodes.Count, szCodes.Count);
            for (int i = 0; i < maxLen && codes.Count < targetCodes; i++)
            {
                if (i < shCodes.Count)
                    codes.Add(shCodes[i]);
                if (i < szCodes.Count && codes.Count < targetCodes)
                    codes.Add(szCodes[i]);
            }
        }
        else if (market == "SH")
        {
            // 上交所主板：600000-603999
            for (int i = 600000; i <= 603999 && codes.Count < targetCodes; i++)
                codes.Add(i.ToString());
            // 科创板：688000-689999
            for (int i = 688000; i <= 689999 && codes.Count < targetCodes; i++)
                codes.Add(i.ToString());
        }
        else if (market == "SZ")
        {
            // 深交所主板：000001-002999
            for (int i = 1; i <= 2999 && codes.Count < targetCodes; i++)
                codes.Add(i.ToString("D6"));
            // 创业板：300000-300999
            for (int i = 300000; i <= 300999 && codes.Count < targetCodes; i++)
                codes.Add(i.ToString());
        }
        
        return codes;
    }

    /// <summary>
    /// 从东方财富获取指定市场的所有股票实时行情（用于选股）
    /// </summary>
    public async Task<List<Stock>> FetchAllStocksFromEastMoneyAsync(string? market = null, int maxCount = 5000)
    {
        var allStocks = new List<Stock>();
        
        try
        {
            _logger.LogInformation("开始从东方财富获取股票列表，市场: {Market}, 最大数量: {MaxCount}", 
                market ?? "全部", maxCount);
            
            // 构建筛选条件
            // m:1 表示上交所, m:2 表示深交所, 不指定则获取全部
            string fs = "";
            if (!string.IsNullOrEmpty(market))
            {
                if (market == "SH")
                {
                    fs = "m:1"; // 上交所
                }
                else if (market == "SZ")
                {
                    fs = "m:2"; // 深交所
                }
            }
            else
            {
                fs = "m:1+t:2"; // 全部A股（上交所+深交所）
            }
            
            int pageSize = 100; // 每页100只股票
            int pageNum = 1;
            int totalFetched = 0;
            
            // 字段说明：
            // f57: 代码, f58: 名称, f43: 最新价, f44: 最高价, f45: 最低价
            // f46: 今开, f60: 昨收, f47: 成交量, f48: 成交额
            // f170: 涨跌幅, f169: 涨跌额, f168: 换手率
            // f162: 市盈率(PE), f167: 市净率(PB)
            string fields = "f57,f58,f43,f44,f45,f46,f60,f47,f48,f170,f169,f168,f162,f167";
            
            while (totalFetched < maxCount)
            {
                // 东方财富股票列表API
                // pn: 页码, pz: 每页数量, po: 排序(1=降序), np: 1
                // fltt: 2, invt: 2, fid: f3(按涨跌幅排序), fs: 筛选条件
                // fields: 需要获取的字段
                var url = $"http://82.push2.eastmoney.com/api/qt/clist/get?" +
                    $"pn={pageNum}&pz={pageSize}&po=1&np=1&fltt=2&invt=2&fid=f3&fs={fs}&fields={fields}";
                
                _logger.LogDebug("请求东方财富股票列表，页码: {PageNum}, URL: {Url}", pageNum, url);
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
                
                var response = await _httpClient.GetStringAsync(url);
                
                dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
                
                if (data?.data == null)
                {
                    _logger.LogWarning("东方财富返回数据为空，页码: {PageNum}", pageNum);
                    break;
                }
                
                if (data.data.diff == null)
                {
                    _logger.LogWarning("东方财富返回diff为空，页码: {PageNum}", pageNum);
                    break;
                }
                
                var stocks = data.data.diff;
                int pageCount = 0;
                
                foreach (var stockInfo in stocks)
                {
                    try
                    {
                        // 获取股票代码（格式：1.600000 或 0.000001）
                        string? secid = stockInfo.f57?.ToString();
                        if (string.IsNullOrEmpty(secid))
                            continue;
                        
                        // 解析secid获取市场代码和股票代码
                        var parts = secid.Split('.');
                        if (parts.Length != 2)
                            continue;
                        
                        string marketCode = parts[0];
                        string stockCode = parts[1];
                        
                        // 判断市场
                        string marketType = marketCode == "1" ? "SH" : "SZ";
                        
                        // 只处理A股（排除B股等）
                        // B股: 90开头（上交所B股），20开头（深交所B股）
                        // 排除其他非A股代码
                        if (stockCode.StartsWith("90") || stockCode.StartsWith("20"))
                            continue;
                        
                        // clist接口的价格格式：通常直接是元，但某些字段可能需要除以100
                        // f43: 最新价（可能需要除以100）, f60: 昨收（可能需要除以100）
                        // 其他价格字段（f44,f45,f46）格式一致
                        decimal currentPriceRaw = SafeConvertToDecimal(stockInfo.f43);
                        decimal closePriceRaw = SafeConvertToDecimal(stockInfo.f60);
                        
                        // clist接口的价格通常需要除以100（单位是分）
                        decimal currentPrice = currentPriceRaw / 100;
                        decimal closePrice = closePriceRaw / 100;
                        decimal openPrice = SafeConvertToDecimal(stockInfo.f46) / 100;
                        decimal highPrice = SafeConvertToDecimal(stockInfo.f44) / 100;
                        decimal lowPrice = SafeConvertToDecimal(stockInfo.f45) / 100;
                        
                        // 价格回退逻辑：非交易时间使用昨收价
                        if (currentPrice == 0 && closePrice > 0)
                            currentPrice = closePrice;
                        if (openPrice == 0 && closePrice > 0)
                            openPrice = closePrice;
                        if (highPrice == 0)
                            highPrice = currentPrice;
                        if (lowPrice == 0)
                            lowPrice = currentPrice;
                        
                        // 获取PE和PB（可能为0、负数或"-"，需要处理）
                        decimal peValue = SafeConvertToDecimal(stockInfo.f162);
                        decimal pbValue = SafeConvertToDecimal(stockInfo.f167);
                        
                        var stock = new Stock
                        {
                            Code = stockCode,
                            Name = stockInfo.f58?.ToString() ?? "未知",
                            Market = marketType,
                            CurrentPrice = currentPrice,
                            OpenPrice = openPrice,
                            ClosePrice = closePrice,
                            HighPrice = highPrice,
                            LowPrice = lowPrice,
                            Volume = SafeConvertToDecimal(stockInfo.f47),
                            Turnover = SafeConvertToDecimal(stockInfo.f48),
                            ChangeAmount = SafeConvertToDecimal(stockInfo.f169) / 100, // 涨跌额也需要除以100
                            ChangePercent = SafeConvertToDecimal(stockInfo.f170),
                            TurnoverRate = SafeConvertToDecimal(stockInfo.f168),
                            PE = peValue > 0 ? peValue : null,
                            PB = pbValue > 0 ? pbValue : null,
                            LastUpdate = DateTime.Now
                        };
                        
                        // 跳过价格为0或无效的股票（可能是停牌、退市等）
                        if (stock.CurrentPrice <= 0 || string.IsNullOrEmpty(stock.Name) || stock.Name == "未知")
                            continue;
                        
                        allStocks.Add(stock);
                        pageCount++;
                        totalFetched++;
                        
                        if (totalFetched >= maxCount)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("解析股票数据失败: {Error}", ex.Message);
                        continue;
                    }
                }
                
                _logger.LogInformation("第 {PageNum} 页获取到 {Count} 只股票", pageNum, pageCount);
                
                // 如果这一页获取的股票数量少于页面大小，说明已经是最后一页
                if (pageCount < pageSize || totalFetched >= maxCount)
                    break;
                
                pageNum++;
                
                // 添加延迟，避免请求过快
                await Task.Delay(200);
            }
            
            _logger.LogInformation("从东方财富总共获取到 {Count} 只股票", allStocks.Count);
            return allStocks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从东方财富获取股票列表失败");
            return allStocks; // 返回已获取的部分数据
        }
    }
}


