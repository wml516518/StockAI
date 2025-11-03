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
            _logger.LogInformation("开始从腾讯财经获取股票列表，市场: {Market}, 最大数量: {MaxCount}", 
                market ?? "全部", maxCount);
            
            var stockCodes = GenerateStockCodeList(market, maxCount * 2);
            _logger.LogInformation("生成 {Count} 个股票代码", stockCodes.Count);
            
            int batchSize = 100;
            for (int i = 0; i < stockCodes.Count && allStocks.Count < maxCount; i += batchSize)
            {
                var batch = stockCodes.Skip(i).Take(batchSize).ToList();
                var batchStocks = await FetchBatchFromTencentAsync(batch);
                
                foreach (var stock in batchStocks)
                {
                    if (stock != null && !string.IsNullOrWhiteSpace(stock.Code) && 
                        !string.IsNullOrWhiteSpace(stock.Name) && stock.CurrentPrice > 0)
                    {
                        allStocks.Add(stock);
                        if (allStocks.Count >= maxCount)
                            break;
                    }
                }
                
                await Task.Delay(300);
                
                if ((i + batchSize) % 500 == 0 || allStocks.Count % 500 == 0)
                {
                    _logger.LogInformation("已获取 {Count}/{Total} 只有效股票", allStocks.Count, Math.Min(stockCodes.Count, maxCount));
                }
            }
            
            _logger.LogInformation("从腾讯财经获取到 {Count} 只有效股票", allStocks.Count);
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
                    if (parts.Length < 33) continue;
                    
                    // 根据格式调整索引
                    // 格式1：parts[0]="1", parts[1]=名称, parts[2]=代码, parts[3]=当前价, parts[4]=昨收, parts[5]=今开
                    // 格式2：parts[0]=代码, parts[1]=当前价, parts[2]=今开, parts[3]=昨收（注意：格式2没有名称字段）
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
                    else if (parts.Length > 0 && parts[0].Length == 6 && parts[0].All(char.IsDigit))
                    {
                        // 格式2：~603901~12.81~12.89~12.90~...
                        // parts[0]=代码, parts[1]=当前价, parts[2]=今开, parts[3]=昨收
                        code = parts[0];
                        priceIndexOffset = -2; // 价格索引需要调整，因为格式不同
                        
                        // 对于格式2，我们需要通过单只股票API获取名称（因为没有名称字段）
                        // 这里先标记为"未知"，后续可以通过API补充
                        name = "未知";
                    }
                    else
                    {
                        // 无法识别格式，跳过
                        continue;
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
                    
                    // 如果名称是"未知"（格式2），暂时跳过，或者尝试从数据库获取
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
                        }
                        catch
                        {
                            // 如果数据库中没有，暂时跳过这条记录
                            continue;
                        }
                    }
                    
                    // 验证名称有效性
                    if (string.IsNullOrWhiteSpace(name) || name == "未知" || name == "N/A" || name == "-")
                        continue;
                    
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
    /// 生成股票代码列表
    /// </summary>
    private List<string> GenerateStockCodeList(string? market, int maxCount)
    {
        var codes = new List<string>();
        
        if (market == null || market == "SH")
        {
            for (int i = 600000; i <= 603999 && codes.Count < maxCount; i++)
                codes.Add(i.ToString());
            for (int i = 688000; i <= 689999 && codes.Count < maxCount; i++)
                codes.Add(i.ToString());
        }
        
        if (market == null || market == "SZ")
        {
            for (int i = 1; i <= 2999 && codes.Count < maxCount; i++)
                codes.Add(i.ToString("D6"));
            for (int i = 300000; i <= 300999 && codes.Count < maxCount; i++)
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
                
                if (data?.data == null || data.data.diff == null)
                {
                    _logger.LogWarning("东方财富返回数据为空，页码: {PageNum}", pageNum);
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


