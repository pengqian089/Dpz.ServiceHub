# ============================================
# 持续输出测试脚本
# 用于测试终端的性能、增量更新和防闪烁机制
# ============================================

param(
    [int]$IntervalMs = 500,  # 输出间隔（毫秒）
    [int]$MaxLines = 100      # 最大输出行数（0=无限）
)

Write-Host "========== 持续输出测试 ==========" -ForegroundColor Cyan
Write-Host "输出间隔: $IntervalMs 毫秒" -ForegroundColor Yellow
Write-Host "最大行数: $(if($MaxLines -eq 0){'无限'}else{$MaxLines})" -ForegroundColor Yellow
Write-Host "按 Ctrl+C 停止`n" -ForegroundColor Gray

$count = 0
$colors = @(
    @{Code=31; Name="红色"},
    @{Code=32; Name="绿色"},
    @{Code=33; Name="黄色"},
    @{Code=34; Name="蓝色"},
    @{Code=35; Name="品红"},
    @{Code=36; Name="青色"}
)

$logLevels = @(
    @{Code=92; Name="INFO"; Weight=50},
    @{Code=93; Name="WARN"; Weight=30},
    @{Code=91; Name="ERROR"; Weight=15},
    @{Code=96; Name="DEBUG"; Weight=5}
)

while ($true) {
    $count++
    
    # 如果达到最大行数，停止
    if ($MaxLines -gt 0 -and $count -gt $MaxLines) {
        break
    }
    
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    
    # 随机选择日志级别（带权重）
    $random = Get-Random -Minimum 1 -Maximum 100
    $level = if ($random -le 50) { $logLevels[0] }
             elseif ($random -le 80) { $logLevels[1] }
             elseif ($random -le 95) { $logLevels[2] }
             else { $logLevels[3] }
    
    # 生成随机消息
    $messages = @(
        "处理HTTP请求: GET /api/users",
        "数据库查询执行: SELECT * FROM Users",
        "缓存命中率: $(Get-Random -Minimum 70 -Maximum 99)%",
        "响应时间: $(Get-Random -Minimum 10 -Maximum 500)ms",
        "活动连接数: $(Get-Random -Minimum 5 -Maximum 50)",
        "内存使用: $(Get-Random -Minimum 200 -Maximum 800)MB",
        "CPU使用率: $(Get-Random -Minimum 10 -Maximum 90)%",
        "请求队列长度: $(Get-Random -Minimum 0 -Maximum 20)",
        "处理事件: UserLoginEvent",
        "发送邮件通知到: user@example.com",
        "文件上传完成: document.pdf ($(Get-Random -Minimum 100 -Maximum 9999)KB)",
        "WebSocket连接建立: wss://localhost:8080",
        "定时任务触发: DailyBackupJob",
        "OAuth认证成功: user_$(Get-Random -Minimum 1000 -Maximum 9999)"
    )
    
    $message = $messages | Get-Random
    
    # 特殊处理ERROR日志，添加更多详细信息
    if ($level.Name -eq "ERROR") {
        $errors = @(
            "NullReferenceException: Object reference not set to an instance",
            "TimeoutException: The operation has timed out",
            "IOException: The process cannot access the file",
            "SqlException: Cannot open database connection"
        )
        $message = "$message - $($errors | Get-Random)"
    }
    
    # 构建彩色日志行
    $logLine = "`e[90m[$timestamp]`e[0m `e[$($level.Code)m[$($level.Name)]`e[0m $message"
    
    # 每10行输出一个特殊的分隔标记
    if ($count % 10 -eq 0) {
        Write-Host "`e[90m" + ("─" * 70) + " $count 行`e[0m"
    }
    
    Write-Host $logLine
    
    # 每20行输出一个包含URL的日志
    if ($count % 20 -eq 0) {
        Write-Host "`e[90m[$timestamp]`e[0m `e[96m[DEBUG]`e[0m 访问API: https://api.example.com/v1/data?id=$count"
    }
    
    # 等待指定时间
    Start-Sleep -Milliseconds $IntervalMs
}

Write-Host "`n========== 测试完成 ==========" -ForegroundColor Green
Write-Host "总共输出了 $count 行日志" -ForegroundColor Cyan
