# Cent Browser 便携助手

> 一句话：带走浏览器，换台电脑解压就能用

把 Cent Browser 浏览器的用户书签、历史、设置和扩展，按你的选择打成带密码的自解压 exe 或压缩包，送到 SMB、WebDAV、FTP 或本地文件夹。带走浏览器，换一台电脑解压就能用。

浏览器保存的密码、Cookie、缓存和自动填充不会带走。打开自动备份并设好内容后，浏览器空闲时会在后台默默备好，不必再管，也几乎不占资源。

也能当瘦身、提取工具用：缓存这些累赘都留在原地，包一下子就轻了 ～(´▽｀)

## 运行要求

- Windows 10 / 11 x64
- 不带运行时的包需要 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。自带运行时的包不用另装

## 下载

- [不带运行时](https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.6/CentBrowser-PortableAssistant-v1.6.exe)（5.8 MB，需要 .NET 8 桌面运行时）
- [自带运行时](https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.6/CentBrowser-PortableAssistant-v1.6-selfcontained.exe)（70.7 MB，不用另装运行时）

## 使用

主界面分三栏六步，一屏看完，改动自动保存。

![主界面](assets/main.png)

### 使用说明

  - **备份过程**：提取、打包、上传三个阶段
  - **快速备份**：正常优先级、上传不限速，最快
  - **慢速备份**：打包降优先级，上传按限速走，不影响使用
  - >本地文件夹是磁盘复制，不受限速影响
    >主页面固定快速，自动备份固定慢速，托盘菜单的备份可以自己选模式
  - **开机启动**：常驻托盘
  - **自动备份**：关掉浏览器 2 分钟后慢速备份。所选内容没变会跳过
  - > 自动备份时提取期间打开浏览器会打断，下次关闭时重试
  - **重置**：删掉设置和还没传完的文件，回到刚开始用的状态。备份记录保留
  - **卸载**：重置+删掉备份记录、工具，然后退出

### 注意

- 备份时需关闭浏览器。提取途中又打开浏览器会停下，等下次关闭再试。打包时可以照常用浏览器

## 构建

```powershell
.\build.ps1                 # 测试版，不带运行时
.\build.ps1 -Bump Release   # 正式版，同时出不带运行时和自带运行时两个包
```

版本号由 `build.ps1` 维护在根目录的 `VERSION` 文件里，不要手动改。

## 捐赠

| 微信扫码 |
| :---: |
| <img src="./assets/donate-wechat.jpg" alt="微信赞赏码" width="320"> |
| 觉得有用，可以请作者喝一杯咖啡<br>自愿，不影响使用 |

## 第三方组件

- [7-Zip](https://www.7-zip.org/) 25.01，GNU LGPL，见 `third_party/7zip/License.txt`
- [FluentFTP](https://github.com/robinrodricks/FluentFTP)，MIT
- [SharpVectors](https://github.com/ElinamLLC/SharpVectors) 1.8.6，BSD-3-Clause
