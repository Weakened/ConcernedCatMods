using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Surroundings;

namespace TheConcernedCat.Companions.Persistence;

/// <summary>Where the doors companions may use are kept: one small file per
/// world, beside the companion sidecars, on this computer only.
///
/// It fails safe in the one direction that matters. A file that cannot be read
/// loads as "no doors allowed" and is never written over, so the worst a
/// damaged or unreadable file can do is keep a companion outside - it can never
/// let one into a house nobody opened to him, and it can never destroy the
/// file somebody might still recover.</summary>
internal sealed class DoorAccessStore
{
    private const string Prefix = "doors-";
    private const string Extension = ".tsv";
    private const string TemporarySuffix = ".tmp";

    private readonly string _rootDirectory;

    public DoorAccessStore(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("A door access root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    public sealed class LoadReport
    {
        public LoadReport(DoorAccessBook book, bool readOnly, int skipped, string? notice)
        {
            Book = book;
            ReadOnly = readOnly;
            Skipped = skipped;
            Notice = notice;
        }

        public DoorAccessBook Book { get; }

        /// <summary>True when the file exists but could not be read; saving is
        /// then refused so it is not overwritten.</summary>
        public bool ReadOnly { get; }

        public int Skipped { get; }

        public string? Notice { get; }
    }

    public string ResolvePath(WorldId world)
    {
        return Path.Combine(_rootDirectory, Prefix + world.ToStorageToken() + Extension);
    }

    public LoadReport Load(WorldId world)
    {
        string path = ResolvePath(world);
        try
        {
            if (!File.Exists(path))
            {
                return new LoadReport(new DoorAccessBook(), readOnly: false, skipped: 0, notice: null);
            }

            DoorAccessBook book = DoorAccessCodec.Parse(File.ReadAllLines(path), out int skipped);
            return new LoadReport(
                book,
                readOnly: false,
                skipped,
                skipped == 0
                    ? null
                    : skipped.ToString(CultureInfo.InvariantCulture) + " damaged door entr" +
                      (skipped == 1 ? "y was" : "ies were") + " skipped; those doors are closed to " +
                      "companions until you allow them again.");
        }
        catch (Exception exception)
        {
            return new LoadReport(
                new DoorAccessBook(),
                readOnly: true,
                skipped: 0,
                "Could not read which doors companions may use (" + exception.GetType().Name + "). " +
                "Every door is closed to them this session, and the file was left as it is.");
        }
    }

    /// <summary>Writes the book if it changed. Returns null on success or when
    /// there was nothing to do, and a notice when it could not be saved.
    /// </summary>
    public string? Save(WorldId world, DoorAccessBook book, bool readOnly)
    {
        if (book == null)
        {
            throw new ArgumentNullException(nameof(book));
        }

        if (!book.IsDirty)
        {
            return null;
        }

        if (readOnly)
        {
            return "The door list was not saved: the existing file could not be read, and saving " +
                "over it could lose it.";
        }

        string path = ResolvePath(world);
        string temporaryPath = path + TemporarySuffix;
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            File.WriteAllLines(temporaryPath, new List<string>(DoorAccessCodec.Serialize(book)));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, destinationBackupFileName: null);
                }
                catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, path, overwrite: true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            book.MarkClean();
            return null;
        }
        catch (Exception exception)
        {
            return "The door list could not be saved (" + exception.GetType().Name + "); the change " +
                "holds for this session only.";
        }
    }
}
