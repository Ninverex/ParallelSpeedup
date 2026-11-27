"""Summarize measured CSV data. Never generates artificial benchmark results."""
import argparse
from pathlib import Path
import warnings
import numpy as np
import pandas as pd
from scipy.optimize import curve_fit
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

KEY = ["experiment", "algorithm", "variant", "n", "segment_size", "seed"]

def amdahl(p, f):
    return 1.0 / (f + (1.0 - f) / p)


def load(folder):
    paths = sorted(folder.glob("*.csv")) if folder.is_dir() else [folder]
    if not paths:
        raise ValueError(f"No CSV files in {folder}")
    df = pd.concat([pd.read_csv(p) for p in paths], ignore_index=True)
    required = set(KEY + ["p", "run", "time_ms", "result", "correct", "abs_error", "error_over_se"])
    if missing := required - set(df.columns):
        raise ValueError(f"Missing columns: {sorted(missing)}")
    if df.duplicated(KEY + ["p", "run"]).any():
        raise ValueError("Duplicate measurements: analyze one measurement session at a time.")
    valid = df.correct.astype(str).str.lower().eq("true")
    if not valid.all():
        raise ValueError("Incorrect or unverified results present. Check the raw data before analysis.")
    if not np.isfinite(df.time_ms).all() or (df.time_ms <= 0).any():
        raise ValueError("Invalid measurement times.")
    return df


def summarize(df):
    stats = df.groupby(KEY + ["p"], as_index=False).agg(
        repeats=("time_ms", "size"), median_ms=("time_ms", "median"),
        min_ms=("time_ms", "min"), max_ms=("time_ms", "max"),
        q1_ms=("time_ms", lambda s: s.quantile(.25)), q3_ms=("time_ms", lambda s: s.quantile(.75)),
        median_abs_error=("abs_error", "median"), max_error_over_se=("error_over_se", "max"),
        result=("result", "median"))
    stats["iqr_ms"] = stats.q3_ms - stats.q1_ms
    stats["throughput_per_s"] = 1000 * stats.n / stats.median_ms
    stats["speedup"] = np.nan
    stats["efficiency"] = np.nan
    stats["karp_flatt"] = np.nan
    fits = []
    for key, group in stats[stats.experiment == "scaling"].groupby(KEY):
        baseline = group[group.p == 1]
        if baseline.empty:
            warnings.warn(f"Missing p=1 baseline: {key}")
            continue
        idx = group.index
        p = group.p.to_numpy(float)
        s = baseline.median_ms.iloc[0] / group.median_ms.to_numpy(float)
        stats.loc[idx, "speedup"] = s
        stats.loc[idx, "efficiency"] = s / p
        mask = p > 1
        stats.loc[idx[mask], "karp_flatt"] = (1/s[mask] - 1/p[mask]) / (1 - 1/p[mask])
        if not mask.any():
            continue
        for scope, m in [("all", mask), ("physical", mask & (p <= 8))]:
            if m.sum() < 2:
                continue
            f = float(curve_fit(amdahl, p[m], s[m], p0=[0.05], bounds=(0,1))[0][0])
            rmse = float(np.sqrt(np.mean((s[m] - amdahl(p[m], f))**2)))
            fits.append(dict(zip(KEY, key), scope=scope, f=f, rmse=rmse, points=int(m.sum())))
    return stats, pd.DataFrame(fits, columns=KEY + ["scope", "f", "rmse", "points"])


def save(fig, path):
    fig.tight_layout()
    fig.savefig(path, dpi=160)
    plt.close(fig)


def plots(stats, fits, out):
    scaling = stats[stats.experiment == "scaling"]
    for (algo, n, seed), group in scaling.groupby(["algorithm", "n", "seed"]):
        fig, ax = plt.subplots(figsize=(8, 4.8))
        for (variant, segment), g in group.groupby(["variant", "segment_size"]):
            g = g.sort_values("p")
            ax.errorbar(g.p.to_numpy(), g.median_ms.to_numpy(), yerr=np.vstack([(g.median_ms-g.min_ms).to_numpy(), (g.max_ms-g.median_ms).to_numpy()]), marker="o", capsize=3,
                        label=f"{variant}" + (f" ({segment//1024} KiB)" if segment else ""))
        ax.set(yscale="log", xlabel="Requested parallelism p", ylabel="Time (ms), log scale", title=f"{algo}: N={n:,} — median and min/max")
        ax.axvline(8, color="gray", linestyle=":", label="8 physical cores")
        ax.set_xticks(sorted(group.p.unique())); ax.legend(); ax.grid(alpha=.2)
        save(fig, out/f"{algo}_{n}_seed{seed}_time.png")
    for key, g in scaling.groupby(KEY):
        g = g.sort_values("p"); g = g[g.speedup.notna()]
        if len(g) < 2:
            continue
        _, algo, variant, n, seg, seed = key
        prefix = f"{algo}_{variant}_{n}_s{seg}_seed{seed}"
        xs = np.linspace(1, max(g.p), 200)
        for metric, label in [("speedup", "Speed-up S(p)"), ("efficiency", "Efficiency E(p)"), ("karp_flatt", "Karp–Flatt e(p)")]:
            fig, ax = plt.subplots(figsize=(7,4.5))
            ax.plot(g.p, g[metric], "o-", label="Measured")
            if metric == "speedup":
                ax.plot(xs, xs, "--", label="Ideal")
                match = fits
                for k,v in zip(KEY,key): match = match[match[k] == v]
                for _, row in match.iterrows():
                    ax.plot(xs, amdahl(xs,row.f), linestyle="--" if row.scope == "all" else "-.", label=f"Amdahl {row.scope}: f={row.f:.3f}")
            elif metric == "efficiency": ax.axhline(1, linestyle="--", label="Ideal")
            ax.axvline(8, color="gray", linestyle=":", label="8 physical cores")
            ax.set(xlabel="Requested parallelism p", ylabel=label, title=f"{algo} / {variant}: N={n:,}")
            ax.set_xticks(g.p); ax.grid(alpha=.2); ax.legend(); save(fig,out/f"{prefix}_{metric}.png")
    for (n,p,seed), group in stats[stats.experiment == "segment-size"].groupby(["n","p","seed"]):
        g = group.sort_values("segment_size")
        fig, ax = plt.subplots(figsize=(7,4.5))
        ax.plot(g.segment_size/1024, g.median_ms, "o-")
        ax.set_xscale("log", base=2); ax.set(xlabel="Segment buffer (KiB)", ylabel="Median time (ms)", title=f"Sieve: N={n:,}, p={p}")
        ax.set_xticks(g.segment_size/1024, labels=[str(int(x/1024)) for x in g.segment_size]); ax.grid(alpha=.2)
        save(fig,out/f"segment_{n}_p{p}_seed{seed}.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=Path("results/full"))
    parser.add_argument("--out", type=Path, default=Path("plots/full"))
    args = parser.parse_args()
    df = load(args.input)
    stats, fits = summarize(df)
    args.out.mkdir(parents=True, exist_ok=True)
    stats.to_csv(args.out/"summary.csv", index=False)
    fits.to_csv(args.out/"amdahl_fits.csv", index=False)
    plots(stats,fits,args.out)
    if (stats.repeats != 10).any():
        print("NOTE: some cases do not have the 10 measured repeats planned for the final experiment.")
    print(f"Analyzed {len(df)} measured runs in {len(stats)} configurations. Output: {args.out}")

if __name__ == "__main__":
    main()
