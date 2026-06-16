namespace TestPlatform.API.Wpf;

/// <summary>
/// 一次 UI 操作的结果。
/// 成败用类型表达，Message 只用于展示/日志，不再依赖 "错误:" 字符串前缀判断。
/// </summary>
public readonly record struct OpResult(bool Success, string Message)
{
    public static OpResult Ok(string message)   => new(true, message);
    public static OpResult Fail(string message) => new(false, message);

    public override string ToString() => Message;
}
