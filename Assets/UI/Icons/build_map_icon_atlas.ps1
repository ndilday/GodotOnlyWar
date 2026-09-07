param(
    [string]$ProposalDirectory = (Join-Path $PSScriptRoot '..\..\..\Design\Exploration\ForceCommandWorkflows\FactionIconProposals'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'map_icon_atlas.png')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not ('MapIconAtlasBuilder' -as [type])) {
    $drawingAssembly = [System.Drawing.Bitmap].Assembly.Location
    $drawingPrimitivesAssembly = [System.Drawing.Color].Assembly.Location
    $gdiPlusAssembly = Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'
    $windowsCoreAssembly = Join-Path $PSHOME 'System.Private.Windows.Core.dll'
    Add-Type -ReferencedAssemblies @(
        $drawingAssembly,
        $drawingPrimitivesAssembly,
        $gdiPlusAssembly,
        $windowsCoreAssembly
    ) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class MapIconAtlasBuilder
{
    private const int AtlasWidth = 128;
    private const int AtlasHeight = 64;

    public static void Build(
        string imperialPath,
        string playerPath,
        string tyranidPath,
        string cultPath,
        string orkPath,
        string outputPath)
    {
        using (var atlas = new Bitmap(AtlasWidth, AtlasHeight, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(atlas))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;

            // Row 1: the double-wide Imperial mark occupies cells (0,0) and (1,0).
            DrawIcon(graphics, imperialPath, Color.FromArgb(0xF5, 0xD6, 0x85), new Rectangle(0, 0, 64, 32));

            // Row 2: player, Tyranids, Genestealer Cult, Orks.
            DrawIcon(graphics, playerPath, Color.FromArgb(0x63, 0xC7, 0xD7), new Rectangle(0, 32, 32, 32));
            DrawIcon(graphics, tyranidPath, Color.FromArgb(0xFF, 0x00, 0xFF), new Rectangle(32, 32, 32, 32));
            DrawIcon(graphics, cultPath, Color.FromArgb(0x58, 0x58, 0xDD), new Rectangle(64, 32, 32, 32));
            DrawIcon(graphics, orkPath, Color.FromArgb(0x1F, 0x54, 0x29), new Rectangle(96, 32, 32, 32));

            NormalizeCellColor(atlas, new Rectangle(0, 0, 64, 32), Color.FromArgb(0xF5, 0xD6, 0x85));
            NormalizeCellColor(atlas, new Rectangle(0, 32, 32, 32), Color.FromArgb(0x63, 0xC7, 0xD7));
            NormalizeCellColor(atlas, new Rectangle(32, 32, 32, 32), Color.FromArgb(0xFF, 0x00, 0xFF));
            NormalizeCellColor(atlas, new Rectangle(64, 32, 32, 32), Color.FromArgb(0x58, 0x58, 0xDD));
            NormalizeCellColor(atlas, new Rectangle(96, 32, 32, 32), Color.FromArgb(0x1F, 0x54, 0x29));

            atlas.Save(outputPath, ImageFormat.Png);
        }
    }

    private static void NormalizeCellColor(Bitmap atlas, Rectangle cell, Color color)
    {
        for (int y = cell.Top; y < cell.Bottom; y++)
        {
            for (int x = cell.Left; x < cell.Right; x++)
            {
                Color pixel = atlas.GetPixel(x, y);
                if (pixel.A > 0)
                {
                    atlas.SetPixel(x, y, Color.FromArgb(pixel.A, color.R, color.G, color.B));
                }
            }
        }
    }

    private static void DrawIcon(Graphics graphics, string sourcePath, Color color, Rectangle destination)
    {
        using (var source = new Bitmap(sourcePath))
        using (var silhouette = CreateSilhouette(source, color))
        {
            graphics.DrawImage(
                silhouette,
                destination,
                0,
                0,
                silhouette.Width,
                silhouette.Height,
                GraphicsUnit.Pixel);
        }
    }

    private static Bitmap CreateSilhouette(Bitmap source, Color color)
    {
        var silhouette = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Color pixel = source.GetPixel(x, y);
                int value = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));

                // Generated proposal boards use a near-black field. Convert that field and
                // every intentional black cutout to transparency while retaining soft edge
                // coverage for clean 32px downsampling.
                double coverage = Math.Max(0.0, Math.Min(1.0, (value - 14.0) / 42.0));
                coverage = coverage * coverage * (3.0 - (2.0 * coverage));
                int alpha = (int)Math.Round(coverage * 255.0);

                silhouette.SetPixel(x, y, Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        return silhouette;
    }
}
'@
}

$sources = @{
    Imperial = Join-Path $ProposalDirectory 'imperial_command_eagle_proposal_v1.png'
    Player   = Join-Path $ProposalDirectory 'player_space_marine_helmet_proposal_v1.png'
    Tyranid  = Join-Path $ProposalDirectory 'tyranid_face_proposal_v2_faction_color.png'
    Cult     = Join-Path $ProposalDirectory 'genestealer_cult_hooded_head_proposal_v2.png'
    Ork      = Join-Path $ProposalDirectory 'ork_head_proposal_v1.png'
}

foreach ($source in $sources.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Map icon source not found: $source"
    }
}

[MapIconAtlasBuilder]::Build(
    $sources.Imperial,
    $sources.Player,
    $sources.Tyranid,
    $sources.Cult,
    $sources.Ork,
    $OutputPath)

Write-Output "Built $OutputPath (128x64, 32px cells)."
