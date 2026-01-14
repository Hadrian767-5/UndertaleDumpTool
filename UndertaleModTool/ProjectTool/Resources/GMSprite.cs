using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;
using ImageMagick.Drawing;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using static UndertaleModTool.ProjectTool.Resources.GMProject;

namespace UndertaleModTool.ProjectTool.Resources
{
    public class GMSprite : ResourceBase, ISaveable
    {
        public GMSprite() : base()
        {
            parent = new IdPath("Sprites", "folders/", true);
        }

        public enum BboxMode
        {
            Automatic,
            FullImage,
            Manual
        }
        public enum CollisionKind
        {
            Precise,
            Rectangle,
            Ellipse,
            Diamond,
            PrecisePerFrame,
            RotatedRectangle
        }
        public enum Type
        {
            Bitmap,
            SWF,
            Spine
        }
        public enum Origin
        {
            TopLeft,
            TopCentre,
            TopRight,
            MiddleLeft,
            MiddleCentre,
            MiddleRight,
            BottomLeft,
            BottomCentre,
            BottomRight,
            Custom
        }

        public BboxMode bboxMode { get; set; } = BboxMode.Automatic;
        public CollisionKind collisionKind { get; set; } = CollisionKind.Rectangle;
        public Type type { get; set; } = Type.Bitmap;
        public Origin origin { get; set; } = Origin.TopLeft;
        public bool preMultiplyAlpha { get; set; } = false;
        public bool edgeFiltering { get; set; } = false;
        public int collisionTolerance { get; set; } = 0;
        public float swfPrecision { get; set; } = 2.525f;
        public int bbox_left { get; set; }
        public int bbox_right { get; set; }
        public int bbox_top { get; set; }
        public int bbox_bottom { get; set; }
        public bool HTile { get; set; } = false;
        public bool VTile { get; set; } = false;
        public bool For3D { get; set; } = false;
        public bool DynamicTexturePage { get; set; } = false;
        public uint width { get; set; } = 64;
        public uint height { get; set; } = 64;
        public IdPath textureGroupId { get; set; } = new IdPath("Default", "texturegroups/");
        public List<uint> swatchColours { get; }
        public uint gridX { get; set; } = 0;
        public uint gridY { get; set; } = 0;
        public List<GMSpriteFrame> frames { get; set; } = new();
        public GMSequence sequence { get; set; } = new()
        {
            playback = GMSequence.PlaybackType.Looped,
            playbackSpeed = 30.0f
        };
        public List<GMImageLayer> layers { get; set; } = new();
        public GMNineSliceData nineSlice { get; set; } = null;

        // Propriedades para Spine
        public GMSpineData spine { get; set; } = null;

        private int BboxWidth => bbox_right - bbox_left + 1;
        private int BboxHeight => bbox_bottom - bbox_top + 1;

        public void AddFrameFrom(UndertaleTexturePageItem texture, int index)
        {
            if (_framesTrack == null)
            {
                _framesTrack = new();
                sequence.tracks.Add(_framesTrack);
            }

            Dump.UpdateStatus($"{name} - Frame {index}");

            string frameGuid = Dump.ToGUID($"{name}.{index}");
            frames.Add(new GMSpriteFrame { name = frameGuid });

            SpriteFrameKeyframe keyframe = new();
            keyframe.Id = new IdPath(frameGuid, $"sprites/{name}/{name}.yy");
            Keyframe<SpriteFrameKeyframe> keyframeHolder = new();
            keyframeHolder.id = Dump.ToGUID($"{name}.{index}k");
            keyframeHolder.Key = index;
            keyframeHolder.Channels.Add("0", keyframe);
            _framesTrack.keyframes.Keyframes.Add(keyframeHolder);

            if (Dump.Options.sprite_skip_existing && File.Exists(Dump.RelativePath($"sprites/{name}/{frameGuid}.png")))
                return;

            IMagickImage<byte> image;
            if (texture is null)
            {
                if (Dump.Options.sprite_missing_texture)
                {
                    image = new MagickImage(MagickColors.Black, 2, 2);

                    new Drawables()
                        .FillColor(MagickColors.Fuchsia)
                        .Point(0, 0)
                        .Point(1, 1)
                        .Draw(image);

                    var resize = new MagickGeometry((uint)BboxWidth, (uint)BboxHeight);
                    resize.IgnoreAspectRatio = true;
                    image.InterpolativeResize(resize, PixelInterpolateMethod.Nearest);

                    if (BboxWidth != width || BboxHeight != height)
                    {
                        image.BackgroundColor = MagickColors.Transparent;

                        var canvas = new MagickGeometry(-bbox_left, -bbox_top, width, height);
                        canvas.IgnoreAspectRatio = true;
                        image.Extent(canvas);
                    }
                }
                else
                    image = new MagickImage(MagickColors.Transparent, width, height);
            }
            else
                image = Dump.TexWorker.GetTextureFor(texture, $"{name}_{index}.png", true);

            _imageFiles.Add($"{frameGuid}", image);
            _imageFiles.Add($"layers/{frameGuid}/{layers[0].name}", image);
        }

        private GMSpriteFramesTrack _framesTrack;
        private Dictionary<string, IMagickImage<byte>> _imageFiles = new();

        /// <summary>
        /// Exporta os dados do Spine
        /// </summary>
        private void ExportSpineData(UndertaleSprite source)
        {
            Dump.UpdateStatus($"Exporting Spine sprite: {name}");

            // Verificar se é realmente um sprite Spine
            if (!source.IsSpineSprite)
            {
                Dump.UpdateStatus($"Warning: {name} is not a Spine sprite");
                return;
            }

            type = Type.Spine;
            spine = new GMSpineData();

            // Criar diretório do sprite
            string spriteFolder = Dump.RelativePath($"sprites/{name}/");
            Directory.CreateDirectory(spriteFolder);

            // Exportar arquivo .atlas
            if (!string.IsNullOrEmpty(source.SpineAtlas))
            {
                string atlasPath = Path.Combine(spriteFolder, $"{name}.atlas");
                File.WriteAllText(atlasPath, source.SpineAtlas);
                spine.atlasFile = $"{name}.atlas";
            }

            // Exportar arquivo .json
            if (!string.IsNullOrEmpty(source.SpineJSON))
            {
                string jsonPath = Path.Combine(spriteFolder, $"{name}.json");
                File.WriteAllText(jsonPath, source.SpineJSON);
                spine.jsonFile = $"{name}.json";
            }

            // Exportar texturas do Spine
            if (source.SpineTextures != null && source.SpineTextures.Count > 0)
            {
                for (int i = 0; i < source.SpineTextures.Count; i++)
                {
                    var spineTexEntry = source.SpineTextures[i];
                    if (spineTexEntry?.Texture != null)
                    {
                        string textureName = $"{name}_texture_{i}.png";
                        
                        // Usar GetTextureFor para obter a imagem
                        var image = Dump.TexWorker.GetTextureFor(spineTexEntry.Texture, textureName, true);
                        _imageFiles.Add(textureName, image);
                        
                        spine.textureFiles.Add(textureName);
                        
                        Dump.UpdateStatus($"{name} - Spine Texture {i}");
                    }
                }
            }

            // Configurar dimensões básicas (usar primeira textura se disponível)
            if (source.SpineTextures != null && source.SpineTextures.Count > 0 && 
                source.SpineTextures[0]?.Texture != null)
            {
                var firstTexture = source.SpineTextures[0].Texture;
                width = firstTexture.BoundingWidth;
                height = firstTexture.BoundingHeight;
            }
            else
            {
                width = source.Width;
                height = source.Height;
            }

            // Configurar grupo de textura
            if (Dump.Options.project_texturegroups && source.SpineTextures != null && 
                source.SpineTextures.Count > 0 && source.SpineTextures[0]?.Texture != null)
            {
                textureGroupId.SetName(TpageAlign.TextureForOrDefault(source.SpineTextures[0].Texture).GetName());
            }

            // Configurar sequência básica
            sequence.name = name;
            sequence.length = 1;
            (sequence.xorigin, sequence.yorigin) = (source.OriginX, source.OriginY);

            // Configurar layer (Spine não usa layers da mesma forma que sprites normais)
            var layer = new GMImageLayer();
            layer.name = Dump.ToGUID($"{name}.layer");
            layers.Add(layer);

            lock (Dump.ProjectResources)
                Dump.ProjectResources.Add(name, "sprites");
        }

        /// <summary>
        /// Translate an UndertaleSprite to a new GMSprite
        /// </summary>
        public GMSprite(UndertaleSprite source) : this()
        {
            name = source.Name.Content;
            (width, height) = (source.Width, source.Height);

            // Verificar se é um sprite Spine PRIMEIRO
            if (source.IsSpineSprite)
            {
                ExportSpineData(source);
                return;
            }

            if (source.V3NineSlice != null)
                nineSlice = new GMNineSliceData(source.V3NineSlice);

            if (Dump.Options.project_texturegroups)
            {
                For3D = TpageAlign.IsSeparateTexture(source);
                textureGroupId.SetName(TpageAlign.TextureForOrDefault(source).GetName());
            }

            #region Sprite origin

            origin = 0;

            if (source.OriginX == MathF.Floor(width / 2f))
                origin += 1;
            else if (source.OriginX == width)
                origin += 2;
            else if (source.OriginX != 0)
                origin = Origin.Custom;

            if (origin != Origin.Custom)
            {
                if (source.OriginY == MathF.Floor(height / 2f))
                    origin += 3;
                else if (source.OriginY == height)
                    origin += 6;
                else if (source.OriginY != 0)
                    origin = Origin.Custom;
            }

            #endregion
            #region Bounding box

            bbox_left = source.MarginLeft;
            bbox_right = source.MarginRight;
            bbox_bottom = source.MarginBottom;
            bbox_top = source.MarginTop;
            bboxMode = (BboxMode)source.BBoxMode;

            if (source.SepMasks == UndertaleSprite.SepMaskType.Precise)
            {
                if (source.CollisionMasks.Count == 1)
                {
                    collisionKind = CollisionKind.Precise;

                    if (Dump.Options.sprite_shaped_masks && ((bbox_right - bbox_left) > 3) && ((bbox_bottom - bbox_top) > 3))
                    {
                        (int maskWidth, int maskHeight) = source.CalculateMaskDimensions(Dump.Data);
                        IMagickImage<byte> maskImage = TextureWorker.GetCollisionMaskImage(source.CollisionMasks[0], maskWidth, maskHeight);
                        IPixelCollection<byte> pixels = maskImage.GetPixels();

                        MagickImage circleImage = new(MagickColors.Black, (uint)maskWidth, (uint)maskHeight);

                        float centerH = (bbox_left + (bbox_right - 1)) / 2f;
                        float centerV = (bbox_top + (bbox_bottom - 1)) / 2f;
                        float radiusH = ((bbox_right - 1) - bbox_left) / 2f + 0.1f;
                        float radiusV = ((bbox_bottom - 1) - bbox_top) / 2f + 0.1f;

                        new Drawables()
                            .DisableStrokeAntialias()
                            .FillColor(MagickColors.White)
                            .Ellipse(centerH, centerV, radiusH, radiusV, 0, 360)
                            .Draw(circleImage);

                        double similarity = 1 - maskImage.Compare(circleImage, ErrorMetric.MeanAbsolute);
                        if (similarity >= Dump.Options.sprite_shaped_mask_precision)
                        {
                            collisionKind = CollisionKind.Ellipse;
                            goto DoneEarly;
                        }

                        MagickImage diamondImage = new(MagickColors.Black, (uint)maskWidth, (uint)maskHeight);

                        new Drawables()
                            .DisableStrokeAntialias()
                            .FillColor(MagickColors.White)
                            .Polygon(
                                new PointD(centerH, bbox_top + 0.1),
                                new PointD(bbox_left + 0.1, centerV),
                                new PointD(centerH, bbox_bottom - 1.1),
                                new PointD(bbox_right - 1.1, centerV)
                            )
                            .Draw(diamondImage);

                        similarity = 1 - maskImage.Compare(diamondImage, ErrorMetric.MeanAbsolute);
                        if (similarity >= Dump.Options.sprite_shaped_mask_precision)
                            collisionKind = CollisionKind.Diamond;

                        diamondImage.Dispose();

                    DoneEarly:
                        circleImage.Dispose();
                        maskImage.Dispose();
                    }
                }
                else if (source.CollisionMasks.Count > 1)
                    collisionKind = CollisionKind.PrecisePerFrame;
            }
            else if (source.SepMasks == UndertaleSprite.SepMaskType.RotatedRect)
                collisionKind = CollisionKind.RotatedRectangle;

            #endregion
            #region Sequence

            sequence.name = name;
            sequence.playbackSpeed = source.GMS2PlaybackSpeed;
            sequence.playbackSpeedType = (GMSequence.PlaybackSpeedType)source.GMS2PlaybackSpeedType;
            sequence.length = source.Textures.Count;
            (sequence.xorigin, sequence.yorigin) = (source.OriginX, source.OriginY);

            var layer = new GMImageLayer();
            layer.name = Dump.ToGUID($"{name}.layer");
            layers.Add(layer);

            for (var i = 0; i < source.Textures.Count; ++i)
            {
                UndertaleTexturePageItem texture = source.Textures[i].Texture;
                if (texture is null)
                {
                    TpageAlign.ConsoleGroup = true;
                    textureGroupId.SetName(TpageAlign.CONSOLE_GROUP_NAME);

                    if (!Dump.Options.sprite_missing_texture)
                        bboxMode = BboxMode.Manual;
                }
                AddFrameFrom(texture, i);
            }

            #endregion

            lock (Dump.ProjectResources)
                Dump.ProjectResources.Add(name, "sprites");
        }

        /// <summary>
        /// Translate an UndertaleBackground to a new GMSprite
        /// </summary>
        public GMSprite(UndertaleBackground source)
        {
            name = Dump.SafeAssetName($"{source.Name.Content}_sprite");
            (width, height) = (source.Texture.BoundingWidth, source.Texture.BoundingHeight);

            bbox_left = source.Texture.TargetX;
            bbox_top = source.Texture.TargetY;
            bbox_right = source.Texture.TargetX + source.Texture.TargetWidth - 1;
            bbox_bottom = source.Texture.TargetX + source.Texture.TargetHeight - 1;

            if (Dump.Options.project_texturegroups)
                textureGroupId.SetName(TpageAlign.TextureForOrDefault(source).GetName());

            sequence.name = name;
            sequence.length = 1;

            var layer = new GMImageLayer();
            layer.name = Dump.ToGUID($"{name}.layer");
            layers.Add(layer);

            AddFrameFrom(source.Texture, 0);
            parent = new IdPath("Sprites", "folders/Tile Sets/", true);

            lock (Dump.ProjectResources)
                Dump.ProjectResources.Add(name, "sprites");
        }

        /// <summary>
        /// Saves the sprite into GameMaker project format
        /// </summary>
        public void Save(string spriteFolder = null)
        {
            if (spriteFolder == null)
                spriteFolder = $"sprites/{name}/";

            string savePath = Dump.RelativePath(spriteFolder);
            Directory.CreateDirectory(savePath);

            // Salvar imagens
            foreach (var i in _imageFiles)
            {
                string path = Path.Combine(savePath, i.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                
                // Para sprites Spine, não adicionar .png extra se já tiver extensão
                if (type == Type.Spine && path.EndsWith(".png"))
                    TextureWorker.SaveImageToFile(i.Value, path);
                else
                    TextureWorker.SaveImageToFile(i.Value, path + ".png");
            }

            // Salvar .yy
            Dump.ToJsonFile(Path.Join(savePath, $"{name}.yy"), this);
        }
    }

    public class GMSpriteFrame : ResourceBase
    {
        public GMSpriteFrame()
        {
            resourceVersion = "1.1";
        }
    }

    public class GMImageLayer : ResourceBase
    {
        public enum BlendMode
        {
            Normal,
            Add,
            Subtract,
            Multiply
        }
        public bool visible { get; set; } = true;
        public bool isLocked { get; set; } = false;
        public BlendMode blendMode { get; set; } = BlendMode.Normal;
        public float opacity { get; set; } = 100.0f;
        public string displayName = "default";
    }

    public class GMNineSliceData : ResourceBase
    {
        private static uint DEFAULT_GUIDE_COLOUR = 4294902015;
        private static uint DEFAULT_HIGHLIGHT_COLOUR = 1728023040;

        public enum HighlightStyle
        {
            Inverted,
            Overlay
        }
        public enum TileMode
        {
            Stretch,
            Repeat,
            Mirror,
            BlankRepeat,
            Hide
        }

        public int left { get; set; } = 0;
        public int top { get; set; } = 0;
        public int right { get; set; } = 0;
        public int bottom { get; set; } = 0;
        public uint[] guideColour { get; set; } = new uint[] { DEFAULT_GUIDE_COLOUR, DEFAULT_GUIDE_COLOUR, DEFAULT_GUIDE_COLOUR, DEFAULT_GUIDE_COLOUR };
        public uint highlightColour { get; set; } = DEFAULT_HIGHLIGHT_COLOUR;
        public HighlightStyle highlightStyle { get; set; } = HighlightStyle.Inverted;
        public bool enabled { get; set; } = false;
        public TileMode[] tileMode { get; set; } = new TileMode[] { TileMode.Stretch, TileMode.Stretch, TileMode.Stretch, TileMode.Stretch, TileMode.Stretch };
        public object loadedVersion { get; set; } = null;

        public GMNineSliceData(UndertaleSprite.NineSlice source)
        {
            left = source.Left;
            top = source.Top;
            right = source.Right;
            bottom = source.Bottom;
            enabled = source.Enabled;
            for (int i = 0; i < 5; i++)
                tileMode[i] = (TileMode)source.TileModes[i];
        }
    }

    /// <summary>
    /// Classe para armazenar dados do Spine
    /// </summary>
    public class GMSpineData
    {
        public string atlasFile { get; set; }
        public string jsonFile { get; set; }
        public List<string> textureFiles { get; set; } = new();
    }
}
