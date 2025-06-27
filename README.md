# SGCC 国网数据采集

自动采集国家电网 95598 网站电力使用数据，支持定时采集、MQTT 发布为 Home Assistant 可识别的设备/传感器。

## 功能特性

- 🔐 **自动登录**: 支持两种模型使用用户名密码登录（甚至包括短信验证码）登录
- 📊 **数据采集**: 自动采集电费余额、日用电量、月用电量、年用电量等数据
- ⏰ **定时任务**: 支持使用 Cron 表达式配置定时采集周期
- 📡 **MQTT 发布**: 自动发布数据到 MQTT 服务器，支持 Home Assistant 的 MQTT 集成
- 🐳 **Docker 支持**: 提供完整的 Docker 部署方案
- 🔧 **灵活配置**: 支持基于配置的多种登录方式和数据发布方式

## 快速开始

推荐使用 [Docker Compose](#docker-compose) **或** [Docker](#docker) 方式部署运行。

### Docker Compose

新建目录 `sgcc`, 目录下新建 `compose.yml` 和  `appsettings.toml`。

- `compose.yml` 文件内容如下：

```yaml
services:
    sgcc:
        image: IMAGE_TAG
        volumes:
            - ./appsettings.toml:/app/appsettings.toml
        init: true
        restart: unless-stopped
```

- `appsettings.toml` 文件内容可以参考 `appsettings.toml.example` 和[配置说明](#配置说明)。

### Docker

1. 如 [配置说明](#配置说明) 部分所述，编写 `appsettings.toml`。
2. 在 `appsettings.toml` 同目录下执行如下命令：

```bash
docker run -d --init \
 --name=sgcc \
 -v $PWD/appsettings.toml:/app/appsettings.toml \
 --restart=unless-stopped \
 IMAGE_TAG
```

### Native

1. 编辑配置文件 `src/SgccElectricityNet.Worker/appsettings.toml`
2. 使用 .NET SDK 运行：

    ```bash
    cd src/SgccElectricityNet.Worker
    dotnet run
    ```

## 配置说明

### 配置文件

```toml
Schedule = "0 9 * * *" 
# 以 Cron 格式定义的运行周期，这里设置为北京时间的每日 9:00 AM 运行一次
# 具体可以参考 Cron 相关文档和下方说明

[Logging.LogLevel]
Default = "Information"
# 日志记录级别，便于调试；通常无需更改

[Captcha]
Type = "Recognizer" # 通常无需更改
ModelPath = "assets/recognizer_single_cls.onnx" # 通常无需更改

[Fetcher]
Type = "Playwright" # 通常无需更改
Username = "your_username" # 95595.cn 的账号
Password = "your_password (optional: if use SMS)" # 95595.cn 的密码
MaxAttempts = 5 # 登录重试次数上限

[[Publishers]]
Type = "Mqtt" # 只使用 MQTT 来发布数据的情况下，通常无需更改
Host = "localhost" # MQTT 服务器地址
Port = 1883 # MQTT 服务器端口
# 如果 MQTT 服务器需要用户名或密码，可以如下配置
Username = "mqtt_user (ignore this line if n/a)"
Password = "mqtt_password (ignore this line if n/a)"
# 对进阶用户，可以配置多种不同的数据发布方式，定期运行时，将同时调起发布
```

### 定时任务配置

定时任务时区固定为 UTC+8（北京时间），使用 Cron 表达式配置定时执行：

```toml
Schedule = "0 9 * * *"  # 每天上午 9 点
Schedule = "0 */6 * * *"  # 每 6 小时执行一次
Schedule = "0 8,18 * * *"  # 每天上午 8 点和下午 6 点
```

> 推荐配置为一天更新一次；默认为 `0 9 * * *`。

## 数据格式

采集的电力数据包含以下信息：

- **账户余额**：当前电费余额（CNY）
- **更新时间**：日用电量数据的更新时间
- **日用电量**：最近一天的用电量（kWh）
- **年度用电量**：当年累计用电量（kWh）
- **年度电费**：当年累计电费（CNY）
- **月度用电量**：当月累计用电量（kWh）
- **月度电费**：当月累计电费（CNY）
- **最近数日统计**：最近 7 天的日用电量记录（并不发布到 HASS）
- **最近数月统计**：当年各月份的用电量和电费汇总（并不发布到 HASS）

## Home Assistant 集成

应用会自动向 MQTT 服务器发布 Home Assistant 设备发现信息，支持以下传感器：

- `balance`：余额
- `last_update_time`：更新时间
- `last_day_usage`：日用电量
- `current_year_charge`：今年电费
- `current_year_usage`：今年用电量
- `last_month_charge`：上月电费
- `last_month_usage`：上月用电量

## 开发

### 项目结构

```tree
SgccElectricityNet/
├── src/
│   └── SgccElectricityNet.Worker/     # 主应用程序
│       ├── Services/
│       │   ├── Captcha/               # 验证服务
│       │   ├── Fetcher/               # 数据采集
│       │   └── Publishing/            # 数据发布
│       ├── Models/                    # 数据模型
│       └── Invocables/
|           └── UpdateInvocable.cs     # 定时任务
├── test/                              # 测试项目
└── assets/                            # 模型文件
```

### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test test/SgccElectricityNet.Tests.Captcha/
dotnet test test/SgccElectricityNet.Tests.Fetcher/
dotnet test test/SgccElectricityNet.Tests.Publishing/
```

### 性能基准测试

```bash
# 运行验证服务性能基准测试项目
cd test/SgccElectricityNet.Benchmarks.Captcha
dotnet run -c Release
```

## 说明

### 贡献

欢迎提交 Issue 和 Pull Request！

### 免责声明

本工具仅用于个人学习和研究目的。使用本工具时请遵守相关网站的使用条款和法律法规。开发者不对使用本工具产生的任何后果承担责任。
