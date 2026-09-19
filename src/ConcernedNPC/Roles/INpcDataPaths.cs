namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>Where one role's data lives - answered entirely by the role.
///
/// <b>What this interface guarantees: that this library never names a file.</b>
/// It composes no data root, joins no path, and invents no file name. It asks
/// the role, by purpose, and the role answers with a complete absolute path or
/// says it keeps no such file. Saying no is a refusal to run that feature, never
/// an invitation to pick a name.
///
/// <b>Why that is a rule rather than a style.</b> A shipped product decides
/// whether a player is new or returning by looking for its own known files in
/// its own data root. Concerned Cartographer does exactly this, and it has
/// already shipped twice with a bug of this shape: a file appearing in that
/// directory that the product's first-run list does not know makes every brand
/// new installation read as a returning player, and a permanent, silent grant
/// follows. A shared runtime that could drop a book, a marker or a stray
/// <c>.tmp</c> into a role's root is that bug waiting for a caller. It cannot,
/// because it has no way to write a name.
///
/// The same rule, from the other direction: this library never owns a BepInEx
/// <c>ConfigFile</c> either, and never binds a setting. A role binds its own
/// settings, in its own sections, and hands the values across. Between the two,
/// the whole program is a code move rather than a migration.</summary>
public interface INpcDataPaths
{
    /// <summary>The absolute directory this role keeps its data in, composed
    /// entirely by the role. Held for diagnostics and so registration can refuse
    /// a role that has not been given one; never joined to anything here. This
    /// library does not create it, probe it, enumerate it or clean it.</summary>
    string Root { get; }

    /// <summary>The absolute path of one file this role keeps, chosen by the
    /// role. <paramref name="purpose"/> is a token declared by the leaf that
    /// needs it - never a file name, never a fragment of one - and the role maps
    /// it to a path however it likes, including one outside
    /// <see cref="Root"/>.
    ///
    /// Returns false when this role keeps no file for that purpose. There are no
    /// purposes yet: this library reads and writes nothing today, and each leaf
    /// that needs one adds it deliberately, knowing that every role must then
    /// answer it.</summary>
    bool TryResolveFile(string purpose, out string absolutePath);
}
