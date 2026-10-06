namespace HdrCenter {
public static class VideoApplyPolicy {
    public static VideoState Unverified(VideoState before,bool super,bool enabled) {
        var pending=new Feature { Requested=enabled,CanControl=true,Activity="未检测",Detail="已调用应用；控制面板已关闭，保存结果待下次连接确认。" };
        var previous=super?before.Hdr:before.Super;
        var other=new Feature { Enabled=previous.Enabled,Cached=previous.Enabled.HasValue,CanControl=previous.CanControl,Activity="未检测",Detail="控制面板已关闭；保留修改前确认的另一项设置。" };
        return super?new VideoState { Super=pending,Hdr=other }:new VideoState { Super=other,Hdr=pending };
    }
}
}
