using UndertaleModLib.Models;

namespace Neutraled.Builder;

/// <summary>按资源名查找（UTMT 的 IList<T> 无内建 ByName）。</summary>
public static class Find
{
    public static UndertaleGameObject? ByName(this IList<UndertaleGameObject> list, string name) =>
        list.FirstOrDefault(x => x.Name?.Content == name);

    public static UndertaleCode? ByName(this IList<UndertaleCode> list, string name) =>
        list.FirstOrDefault(x => x.Name?.Content == name);

    public static UndertaleScript? ByName(this IList<UndertaleScript> list, string name) =>
        list.FirstOrDefault(x => x.Name?.Content == name);

    public static UndertaleFunction? ByName(this IList<UndertaleFunction> list, string name) =>
        list.FirstOrDefault(x => x.Name?.Content == name);

    public static UndertaleString? ByContent(this IList<UndertaleString> list, string content) =>
        list.FirstOrDefault(x => x.Content == content);
}
