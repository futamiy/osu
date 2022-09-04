// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking.Statistics
{
    /// <summary>
    /// A graph which displays the distribution of hit timing in a series of <see cref="HitEvent"/>s.
    /// </summary>
    public class HitEventTimingDistributionGraph : CompositeDrawable
    {
        /// <summary>
        /// The number of bins on each side of the timing distribution.
        /// </summary>
        private const int timing_distribution_bins = 50;

        /// <summary>
        /// The total number of bins in the timing distribution, including bins on both sides and the centre bin at 0.
        /// </summary>
        private const int total_timing_distribution_bins = timing_distribution_bins * 2 + 1;

        /// <summary>
        /// The centre bin, with a timing distribution very close to/at 0.
        /// </summary>
        private const int timing_distribution_centre_bin_index = timing_distribution_bins;

        /// <summary>
        /// The number of data points shown on each side of the axis below the graph.
        /// </summary>
        private const float axis_points = 5;

        /// <summary>
        /// The currently displayed hit events.
        /// </summary>
        private readonly IReadOnlyList<HitEvent> hitEvents;

        [Resolved]
        private OsuColour colours { get; set; }

        /// <summary>
        /// Creates a new <see cref="HitEventTimingDistributionGraph"/>.
        /// </summary>
        /// <param name="hitEvents">The <see cref="HitEvent"/>s to display the timing distribution of.</param>
        public HitEventTimingDistributionGraph(IReadOnlyList<HitEvent> hitEvents)
        {
            this.hitEvents = hitEvents.Where(e => !(e.HitObject.HitWindows is HitWindows.EmptyHitWindows) && e.Result.IsHit()).ToList();
        }

        private IDictionary<HitResult, int>[] bins;
        private double binSize;
        private double hitOffset;

        private Bar[] barDrawables;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (hitEvents == null || hitEvents.Count == 0)
                return;

            bins = Enumerable.Range(0, total_timing_distribution_bins).Select(_ => new Dictionary<HitResult, int>()).ToArray<IDictionary<HitResult, int>>();

            binSize = Math.Ceiling(hitEvents.Max(e => Math.Abs(e.TimeOffset)) / timing_distribution_bins);

            // Prevent div-by-0 by enforcing a minimum bin size
            binSize = Math.Max(1, binSize);

            Scheduler.AddOnce(updateDisplay);
        }

        public void UpdateOffset(double hitOffset)
        {
            this.hitOffset = hitOffset;
            Scheduler.AddOnce(updateDisplay);
        }

        private void updateDisplay()
        {
            bool roundUp = true;

            foreach (var bin in bins)
                bin.Clear();

            foreach (var e in hitEvents)
            {
                double time = e.TimeOffset + hitOffset;

                double binOffset = time / binSize;

                // .NET's round midpoint handling doesn't provide a behaviour that works amazingly for display
                // purposes here. We want midpoint rounding to roughly distribute evenly to each adjacent bucket
                // so the easiest way is to cycle between downwards and upwards rounding as we process events.
                if (Math.Abs(binOffset - (int)binOffset) == 0.5)
                {
                    binOffset = (int)binOffset + Math.Sign(binOffset) * (roundUp ? 1 : 0);
                    roundUp = !roundUp;
                }

                int index = timing_distribution_centre_bin_index + (int)Math.Round(binOffset, MidpointRounding.AwayFromZero);

                // may be out of range when applying an offset. for such cases we can just drop the results.
                if (index >= 0 && index < bins.Length)
                {
                    bins[index].TryGetValue(e.Result, out int value);
                    bins[index][e.Result] = ++value;
                }
            }

            if (barDrawables != null)
            {
                for (int i = 0; i < barDrawables.Length; i++)
                {
                    barDrawables[i].UpdateOffset(bins[i].Sum(b => b.Value));
                }
            }
            else
            {
                int maxCount = bins.Max(b => b.Values.Sum());
                barDrawables = new Bar[total_timing_distribution_bins];

                for (int i = 0; i < barDrawables.Length; i++)
                {
                    IReadOnlyList<BarValue> values = bins[i].Select(b => new BarValue(b.Key.OrderingIndex(), b.Value, colours.DrawForHitResult(b.Key))).OrderBy(b => b.Index).ToList();
                    barDrawables[i] = new Bar(values, maxCount, i == timing_distribution_centre_bin_index);
                }

                Container axisFlow;

                const float axis_font_size = 12;

                InternalChild = new GridContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.8f,
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Content = new[] { barDrawables }
                            }
                        },
                        new Drawable[]
                        {
                            axisFlow = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = axis_font_size,
                            }
                        },
                    },
                    RowDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(GridSizeMode.AutoSize),
                    }
                };

                // Our axis will contain one centre element + 5 points on each side, each with a value depending on the number of bins * bin size.
                double maxValue = timing_distribution_bins * binSize;
                double axisValueStep = maxValue / axis_points;

                axisFlow.Add(new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "0",
                    Font = OsuFont.GetFont(size: axis_font_size, weight: FontWeight.SemiBold)
                });

                for (int i = 1; i <= axis_points; i++)
                {
                    double axisValue = i * axisValueStep;
                    float position = (float)(axisValue / maxValue);
                    float alpha = 1f - position * 0.8f;

                    axisFlow.Add(new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativePositionAxes = Axes.X,
                        X = -position / 2,
                        Alpha = alpha,
                        Text = axisValue.ToString("-0"),
                        Font = OsuFont.GetFont(size: axis_font_size, weight: FontWeight.SemiBold)
                    });

                    axisFlow.Add(new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativePositionAxes = Axes.X,
                        X = position / 2,
                        Alpha = alpha,
                        Text = axisValue.ToString("+0"),
                        Font = OsuFont.GetFont(size: axis_font_size, weight: FontWeight.SemiBold)
                    });
                }
            }
        }

        private readonly struct BarValue
        {
            public readonly int Index;
            public readonly float Value;
            public readonly Color4 Colour;

            public BarValue(int index, float value, Color4 colour)
            {
                Index = index;
                Value = value;
                Colour = colour;
            }
        }

        private class Bar : CompositeDrawable
        {
            private float totalValue => values.Sum(v => v.Value);
            private float basalHeight => BoundingBox.Width / BoundingBox.Height;
            private float availableHeight => 1 - basalHeight;

            private readonly IReadOnlyList<BarValue> values;
            private readonly float maxValue;

            private readonly Circle[] boxOriginals;
            private Circle boxAdjustment;

            public Bar(IReadOnlyList<BarValue> values, float maxValue, bool isCentre)
            {
                this.values = values;
                this.maxValue = maxValue;

                RelativeSizeAxes = Axes.Both;
                Masking = true;

                if (values.Any())
                {
                    boxOriginals = values.Select(v => new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Colour = isCentre ? Color4.White : v.Colour,
                        Height = 0,
                    }).ToArray();
                    InternalChildren = boxOriginals.Reverse().ToArray();
                }
                else
                {
                    InternalChildren = boxOriginals = new[]
                    {
                        new Circle
                        {
                            RelativeSizeAxes = Axes.Both,
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Colour = isCentre ? Color4.White : Color4.Gray,
                            Height = 0,
                        },
                    };
                }
            }

            private const double total_duration = 300;

            private double duration => total_duration / Math.Max(values.Count, 1);

            private float offsetForValue(float value)
            {
                return availableHeight * value / maxValue;
            }

            private float heightForValue(float value)
            {
                return basalHeight + offsetForValue(value);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                float offsetValue = 0;

                if (values.Any())
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        boxOriginals[i].Y = BoundingBox.Height * offsetForValue(offsetValue);
                        boxOriginals[i].Delay(duration * i).ResizeHeightTo(heightForValue(values[i].Value), duration, Easing.OutQuint);
                        offsetValue -= values[i].Value;
                    }
                }
                else
                    boxOriginals.Single().ResizeHeightTo(basalHeight, duration, Easing.OutQuint);
            }

            public void UpdateOffset(float adjustment)
            {
                bool hasAdjustment = adjustment != totalValue;

                if (boxAdjustment == null)
                {
                    if (!hasAdjustment)
                        return;

                    AddInternal(boxAdjustment = new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Colour = Color4.Yellow,
                        Blending = BlendingParameters.Additive,
                        Alpha = 0.6f,
                        Height = 0,
                    });
                }

                boxAdjustment.ResizeHeightTo(heightForValue(adjustment), duration, Easing.OutQuint);
                boxAdjustment.FadeTo(!hasAdjustment ? 0 : 1, duration, Easing.OutQuint);
            }
        }
    }
}
