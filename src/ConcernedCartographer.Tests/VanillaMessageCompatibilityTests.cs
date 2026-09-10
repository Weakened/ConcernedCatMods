using System.Reflection;
using TheConcernedCat.ConcernedCartographer.Runtime;

// Game-free fixtures compile the real adapter and exercise actual reflection
// over both supported API shapes. They are never included in the plugin.
public class Character
{
    public string? LastText;
    public int LastAmount = -1;
    public bool LastLog = true;
    public bool Throw;
    public void Message(MessageHud.MessageType type, string text, int amount = 0, object? icon = null, bool log = false)
    {
        if (Throw) throw new InvalidOperationException("fixture");
        LastText = text; LastAmount = amount; LastLog = log;
    }
}
public class MessageHud { public enum MessageType { TopLeft } }
namespace HarmonyLib
{
    public delegate object? FastInvokeHandler(object target, params object?[] arguments);
    public static class MethodInvoker
    {
        public static FastInvokeHandler GetHandler(MethodInfo method) =>
            (target, arguments) => method.Invoke(target, arguments);
    }
    public static class AccessTools
    {
        public static List<MethodInfo> GetDeclaredMethods(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).ToList();
    }
}
public class VanillaMessageCompatibilityTests
{
    private class OldGame
    {
        public void Message(MessageHud.MessageType type, string text, int amount = 0, object? icon = null) { }
    }
    private class RequiredTail
    {
        public void Message(MessageHud.MessageType type, string text, int required) { }
    }
    private class ByRefTail
    {
        public void Message(MessageHud.MessageType type, string text, ref int amount) { }
    }
    private class GenericMethod
    {
        public void Message<T>(MessageHud.MessageType type, string text) { }
    }
    private class WrongReturn
    {
        public int Message(MessageHud.MessageType type, string text) => 0;
    }
    private class WrongPrefix
    {
        public void Message(int type, string text) { }
    }
    [Fact]
    public void ResolvesOldFourArgumentSignature() =>
        Assert.Equal(4, VanillaMessage.ResolveMessageMethod(typeof(OldGame))!.GetParameters().Length);
    [Fact]
    public void ResolvesNewFiveArgumentSignature() =>
        Assert.Equal(5, VanillaMessage.ResolveMessageMethod(typeof(Character))!.GetParameters().Length);
    [Theory]
    [InlineData(typeof(RequiredTail))]
    [InlineData(typeof(ByRefTail))]
    [InlineData(typeof(GenericMethod))]
    [InlineData(typeof(WrongReturn))]
    [InlineData(typeof(WrongPrefix))]
    public void RejectsUnsupportedContracts(Type type) => Assert.Null(VanillaMessage.ResolveMessageMethod(type));
    [Fact]
    public void UsesLiveOptionalDefaults()
    {
        var player = new Character();
        Assert.True(VanillaMessage.Available);
        VanillaMessage.Show(player, MessageHud.MessageType.TopLeft, "test");
        Assert.Equal("test", player.LastText);
        Assert.Equal(0, player.LastAmount);
        Assert.False(player.LastLog);
    }
    [Fact]
    public void MissingPlayerIsHarmless() => VanillaMessage.Show(null, MessageHud.MessageType.TopLeft, "test");
    [Fact]
    public void InvocationFailureIsHarmless() => VanillaMessage.Show(new Character { Throw = true }, MessageHud.MessageType.TopLeft, "test");
}
