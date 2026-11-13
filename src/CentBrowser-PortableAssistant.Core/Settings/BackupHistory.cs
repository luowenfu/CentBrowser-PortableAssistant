namespace CentBrowser_PortableAssistant.Core.Settings;

/// <summary>一次成功送达的记录，清理旧备份时按它删除远程文件。</summary>
public sealed class RemoteRecord
{
    public string PlanId { get; set; } = "";

    /// <summary>送达时的方案名。方案后来删了，记录里仍用这个名字。</summary>
    public string PlanName { get; set; } = "";

    public string TargetId { get; set; } = "";

    /// <summary>送达时的目的地名。目的地后来删了，记录里仍用这个名字。</summary>
    public string TargetName { get; set; } = "";

    /// <summary>同一次备份产出的多个文件共用一个批次号。</summary>
    public string BatchId { get; set; } = "";

    public string RemotePath { get; set; } = "";

    /// <summary>这个文件的字节数。早前的记录没有，是 0。</summary>
    public long Size { get; set; }

    public DateTime UploadedUtc { get; set; }

    /// <summary>放进上传队列的时间。记录的时间列用它。早前的记录没有，就用送达时间。</summary>
    public DateTime QueuedUtc { get; set; }

    /// <summary>这次是自动备份。早前的记录没有这项，是 null。</summary>
    public bool? FromAuto { get; set; }

    /// <summary>慢速打包。早前的记录没有这项，是 null，记录里就不写速度。</summary>
    public bool? Slow { get; set; }

    /// <summary>这次备份开始的时间。早前的记录没有。</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>这个目的地开始上传的时间。和 <see cref="UploadedUtc"/> 相减是它自己的用时。早前的记录没有，就退回备份开始时间。</summary>
    public DateTime UploadStartedUtc { get; set; }

    /// <summary>目的地上的文件已按保留份数删掉。记录还留着。</summary>
    public bool FileRemoved { get; set; }
}

public sealed class PlanState
{
    public DateTime? LastSuccessLocal { get; set; }

    public string Fingerprint { get; set; } = "";

    /// <summary>"数据未变化，已跳过"每天只提示一次。</summary>
    public DateOnly? LastUnchangedNotice { get; set; }

    /// <summary>上次各格式打包后的体积 ÷ 提取到的原始大小，键是 7z、zip。用来估算下次压缩后有多大。</summary>
    public Dictionary<string, double> Ratios { get; set; } = [];
}

public sealed class BackupHistory
{
    public Dictionary<string, PlanState> Plans { get; set; } = [];

    public List<RemoteRecord> Records { get; set; } = [];

    /// <summary>没传完就被新备份顶掉的目的地。记录里写成失败。</summary>
    public List<AbandonedTarget> Abandoned { get; set; } = [];

    public PlanState For(string planId)
    {
        if (!Plans.TryGetValue(planId, out var state))
        {
            state = new PlanState();
            Plans[planId] = state;
        }
        return state;
    }
}

/// <summary>一次备份里没送到、后来被新备份顶掉的目的地。</summary>
public sealed class AbandonedTarget
{
    public string BatchId { get; set; } = "";

    public string FileName { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string TargetName { get; set; } = "";

    public string PlanId { get; set; } = "";

    /// <summary>这次备份时的方案名。方案后来删了，记录里仍用这个名字。</summary>
    public string PlanName { get; set; } = "";

    /// <summary>打包进队列的时间。这次一个目的地都没送到时，记录用它排序和显示。</summary>
    public DateTime CreatedUtc { get; set; }

    public bool? FromAuto { get; set; }

    public long Size { get; set; }
}
