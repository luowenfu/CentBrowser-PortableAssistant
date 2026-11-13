namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>一个待上传的本地文件。全部目标都传完才删本地文件。</summary>
public sealed class UploadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string BatchId { get; set; } = "";

    public string PlanId { get; set; } = "";

    public string PlanName { get; set; } = "";

    public string LocalPath { get; set; } = "";

    public string FileName { get; set; } = "";

    public long Size { get; set; }

    public List<string> PendingTargetIds { get; set; } = [];

    /// <summary>打包时选中的目的地。上传过程中不会删，用来判断还在的目的地是否都送到了。</summary>
    public List<string> TargetIds { get; set; } = [];

    /// <summary>这次打包时的数据指纹。上传全部送达后再写入正式记录。旧队列里的任务没有这份，传完时不要覆盖。</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>自动备份带出来的上传。记录里据此写"自动备份"还是"手动备份"。</summary>
    public bool FromAuto { get; set; }

    /// <summary>这次打包让着别的程序。状态上显示成慢速。</summary>
    public bool Slow { get; set; }

    /// <summary>这次备份开始的时间。完成后用来算用时。早前的队列没有，是默认值。</summary>
    public DateTime StartedUtc { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptUtc { get; set; }

    public string LastError { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>第 n 次失败后等多久再试：1、5、15、60 分钟，之后每小时一次。</summary>
    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(60),
    };
}
