import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * The Dashboard's chart primitives — **hand-written inline SVG and CSS, no charting library.**
 *
 * D75 forbids a second UI dependency without an explicit decision, and nothing here justifies one:
 * these are static, non-interactive shapes over at most a dozen points. A charting library would add
 * 50–150 kB to buy pan, zoom, tooltips and animation that a management cockpit does not want — a
 * figure that moves while you read it is harder to read, not easier.
 *
 * Three rules every chart follows:
 *
 * - **Colour is never the only encoding.** Every series is labelled and every figure is printed, so
 *   the chart ranks at a glance while staying exact — and readable for a colour-blind user.
 * - **Charts scale to their own data**, never to a fixed axis and never to a total that would render
 *   every real bar as a sliver.
 * - **No decoration.** Nothing that does not help a decision is drawn.
 */

// -------------------------------------------------------------------------------------------------
// Funnel — the workflow, narrowing
// -------------------------------------------------------------------------------------------------

export interface FunnelRow {
  readonly id: string;
  readonly label: string;
  readonly count: number;
  /** Already formatted, or null where a stage has no money attached yet. */
  readonly value: string | null;
  /** 0–1, relative to the widest stage. */
  readonly share: number;
  /** Carried over from the previous stage, already formatted, or null for the first. */
  readonly conversion: string | null;
}

/**
 * The workflow as a narrowing bar stack: Anfrage → Besichtigung → Angebot → Beim Kunden → Gewonnen.
 *
 * **This is what replaces a Kanban on the Dashboard**, and the swap is the point rather than a style
 * preference. A Kanban is an *inventory* view — it answers "which item is where", a working question
 * that belongs in the Leads workspace. A funnel answers "where does the business lose momentum",
 * which is the management question, and it can carry money and conversion per stage, which a column
 * of cards cannot.
 *
 * Bars scale to the widest stage, so the narrowing is visible even when every stage is small.
 */
@Component({
  selector: 'app-funnel-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="funnel">
      @for (row of rows(); track row.id) {
        <li class="stage">
          <div class="stage__head">
            <span class="stage__label">{{ row.label }}</span>
            @if (row.conversion) {
              <span class="stage__conversion">{{ row.conversion }}</span>
            }
          </div>

          <div class="stage__bar">
            <div class="stage__fill" [style.width.%]="row.share * 100">
              <span class="stage__count">{{ row.count }}</span>
            </div>
            @if (row.value) {
              <span class="stage__value">{{ row.value }}</span>
            }
          </div>
        </li>
      }
    </ol>
  `,
  styles: `
    .funnel {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .stage__head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--rt-space-3);
      margin-bottom: var(--rt-space-2);
    }
    .stage__label {
      font-size: 13px;
      font-weight: 600;
    }
    .stage__conversion {
      font-size: 11.5px;
      font-weight: 650;
      color: var(--rt-text-muted);
      font-variant-numeric: tabular-nums;
    }
    .stage__bar {
      display: flex;
      align-items: center;
      gap: var(--rt-space-3);
    }
    .stage__fill {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      min-width: 46px;
      height: 28px;
      padding-right: var(--rt-space-3);
      border-radius: var(--rt-radius-sm);
      background: linear-gradient(90deg, var(--rt-chart-1) 0%, var(--rt-chart-1-soft) 100%);
    }
    .stage__count {
      font-size: 13.5px;
      font-weight: 700;
      color: #fff;
      font-variant-numeric: tabular-nums;
    }
    .stage__value {
      font-size: 12.5px;
      font-weight: 600;
      color: var(--rt-text-muted);
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
  `,
})
export class FunnelChart {
  readonly rows = input.required<readonly FunnelRow[]>();
}

// -------------------------------------------------------------------------------------------------
// Segmented bar — one total, split by status
// -------------------------------------------------------------------------------------------------

export interface Segment {
  readonly id: string;
  readonly label: string;
  readonly value: number;
  readonly formatted: string;
  /** A `--rt-*` custom property name. */
  readonly color: string;
}

/**
 * A single total split into its parts — invoiced money as paid / open / overdue.
 *
 * **A stacked bar rather than a pie or donut**, because the question is "how much of the whole is
 * each part", and length along one axis is far easier to compare than angle. A donut would also push
 * the three figures into a legend, where a bar carries them inline.
 *
 * A segment below a couple of percent still renders at a visible minimum width, so a small but
 * urgent slice — overdue money, typically — cannot disappear. The printed figure stays exact.
 */
@Component({
  selector: 'app-segmented-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar" role="img" [attr.aria-label]="caption()">
      @for (part of parts(); track part.id) {
        <span
          class="bar__part"
          [style.width.%]="part.width"
          [style.background]="'var(' + part.color + ')'"
        ></span>
      }
    </div>

    <ul class="legend">
      @for (part of parts(); track part.id) {
        <li class="legend__item">
          <span class="legend__swatch" [style.background]="'var(' + part.color + ')'"></span>
          <span class="legend__label">{{ part.label }}</span>
          <span class="legend__value">{{ part.formatted }}</span>
          <span class="legend__share">{{ part.share }} %</span>
        </li>
      }
    </ul>
  `,
  styles: `
    .bar {
      display: flex;
      height: 14px;
      border-radius: var(--rt-radius-pill);
      overflow: hidden;
      background: var(--rt-surface-sunken);
    }
    .bar__part {
      display: block;
      height: 100%;
    }
    .legend {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-3);
      margin: var(--rt-space-5) 0 0;
      padding: 0;
      list-style: none;
    }
    .legend__item {
      display: grid;
      grid-template-columns: 10px 1fr auto 46px;
      align-items: center;
      gap: var(--rt-space-3);
    }
    .legend__swatch {
      width: 10px;
      height: 10px;
      border-radius: 3px;
    }
    .legend__label {
      font-size: 13px;
    }
    .legend__value {
      font-size: 13.5px;
      font-weight: 650;
      font-variant-numeric: tabular-nums;
    }
    .legend__share {
      font-size: 12px;
      color: var(--rt-text-muted);
      font-variant-numeric: tabular-nums;
      text-align: right;
    }
  `,
})
export class SegmentedBar {
  readonly segments = input.required<readonly Segment[]>();
  readonly caption = input('');

  protected readonly parts = computed(() => {
    const segments = this.segments();
    const total = segments.reduce((sum, segment) => sum + segment.value, 0) || 1;

    return segments.map((segment) => ({
      ...segment,
      // A floor, so an urgent sliver stays visible; the printed figure remains exact.
      width: segment.value > 0 ? Math.max(2, (segment.value / total) * 100) : 0,
      share: Math.round((segment.value / total) * 100),
    }));
  });
}
