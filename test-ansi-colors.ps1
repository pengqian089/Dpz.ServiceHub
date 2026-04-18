# ============================================
# ANSI 颜色测试脚本
# 用于测试 xterm.js 终端的ANSI转义序列支持
# ============================================

Write-Host "`n========== ANSI 颜色支持测试 ==========" -ForegroundColor Cyan

# 测试基本16色（前景色）
Write-Host "`n--- 基本16色（前景色） ---"
Write-Host "`e[30m黑色文本`e[0m"
Write-Host "`e[31m红色文本`e[0m"
Write-Host "`e[32m绿色文本`e[0m"
Write-Host "`e[33m黄色文本`e[0m"
Write-Host "`e[34m蓝色文本`e[0m"
Write-Host "`e[35m品红文本`e[0m"
Write-Host "`e[36m青色文本`e[0m"
Write-Host "`e[37m白色文本`e[0m"

# 测试亮色（90-97）
Write-Host "`n--- 亮色变体（90-97） ---"
Write-Host "`e[90m亮黑色（灰色）`e[0m"
Write-Host "`e[91m亮红色`e[0m"
Write-Host "`e[92m亮绿色`e[0m"
Write-Host "`e[93m亮黄色`e[0m"
Write-Host "`e[94m亮蓝色`e[0m"
Write-Host "`e[95m亮品红`e[0m"
Write-Host "`e[96m亮青色`e[0m"
Write-Host "`e[97m亮白色`e[0m"

# 测试背景色
Write-Host "`n--- 背景色测试 ---"
Write-Host "`e[41m 红色背景 `e[0m"
Write-Host "`e[42m 绿色背景 `e[0m"
Write-Host "`e[43m 黄色背景 `e[0m"
Write-Host "`e[44m 蓝色背景 `e[0m"
Write-Host "`e[45m 品红背景 `e[0m"
Write-Host "`e[46m 青色背景 `e[0m"
Write-Host "`e[47m 白色背景 `e[0m"

# 测试文本样式
Write-Host "`n--- 文本样式 ---"
Write-Host "`e[1m粗体文本`e[0m"
Write-Host "`e[2m暗淡文本`e[0m"
Write-Host "`e[3m斜体文本`e[0m"
Write-Host "`e[4m下划线文本`e[0m"
Write-Host "`e[9m删除线文本`e[0m"

# 测试组合效果
Write-Host "`n--- 组合效果 ---"
Write-Host "`e[1;31m粗体红色`e[0m"
Write-Host "`e[4;32m下划线绿色`e[0m"
Write-Host "`e[1;4;33m粗体下划线黄色`e[0m"
Write-Host "`e[97;41m白色文本红色背景`e[0m"
Write-Host "`e[30;47m黑色文本白色背景`e[0m"

# 测试256色（如果支持）
Write-Host "`n--- 256色测试（前景色） ---"
Write-Host "`e[38;5;196m256色红色`e[0m"
Write-Host "`e[38;5;46m256色绿色`e[0m"
Write-Host "`e[38;5;21m256色蓝色`e[0m"
Write-Host "`e[38;5;201m256色品红`e[0m"
Write-Host "`e[38;5;226m256色黄色`e[0m"
Write-Host "`e[38;5;51m256色青色`e[0m"

# 测试RGB真彩色（如果支持）
Write-Host "`n--- RGB真彩色测试 ---"
Write-Host "`e[38;2;255;100;100mRGB红色(255,100,100)`e[0m"
Write-Host "`e[38;2;100;255;100mRGB绿色(100,255,100)`e[0m"
Write-Host "`e[38;2;100;100;255mRGB蓝色(100,100,255)`e[0m"
Write-Host "`e[38;2;255;165;0mRGB橙色(255,165,0)`e[0m"
Write-Host "`e[38;2;128;0;128mRGB紫色(128,0,128)`e[0m"

# 模拟真实的日志输出
Write-Host "`n--- 模拟实际日志输出 ---"
Write-Host "`e[90m[12:34:56]`e[0m `e[92m[INFO]`e[0m 应用程序启动成功"
Write-Host "`e[90m[12:34:57]`e[0m `e[93m[WARN]`e[0m 配置文件未找到，使用默认配置"
Write-Host "`e[90m[12:34:58]`e[0m `e[91m[ERROR]`e[0m 数据库连接失败: Connection timeout"
Write-Host "`e[90m[12:34:59]`e[0m `e[96m[DEBUG]`e[0m 加载模块: `e[4mUserManagement`e[0m"
Write-Host "`e[90m[12:35:00]`e[0m `e[92m[INFO]`e[0m 服务监听地址: `e[36mhttp://localhost:5000`e[0m"

# ASP.NET Core风格的日志
Write-Host "`n--- ASP.NET Core风格日志 ---"
Write-Host "`e[90minfo`e[0m: Microsoft.Hosting.Lifetime[0]"
Write-Host "      `e[92mNow listening on: https://localhost:5001`e[0m"
Write-Host "`e[90minfo`e[0m: Microsoft.Hosting.Lifetime[0]"
Write-Host "      `e[92mNow listening on: http://localhost:5000`e[0m"
Write-Host "`e[90minfo`e[0m: Microsoft.Hosting.Lifetime[0]"
Write-Host "      Application started. Press Ctrl+C to shut down."
Write-Host "`e[33mwarn`e[0m: Microsoft.AspNetCore.DataProtection[35]"
Write-Host "      Using an ephemeral key repository. Keys will not be persisted."

# 测试URL和路径识别（xterm-addon-web-links）
Write-Host "`n--- URL识别测试 ---"
Write-Host "官方网站: https://github.com/pengqian089"
Write-Host "API文档: http://localhost:5000/swagger"
Write-Host "项目地址: https://docs.avaloniaui.net/"

Write-Host "`n========== 测试完成 ==========" -ForegroundColor Cyan
Write-Host "如果看到了丰富的颜色，说明ANSI支持正常！`n" -ForegroundColor Green
