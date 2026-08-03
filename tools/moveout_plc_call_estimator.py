#!/usr/bin/env python3
"""Estimate PLC read pressure for Serial_MoveOutAndTestResults."""

import argparse


HEADER_READS = 8
FIELDS_PER_MEASURE = 9


def legacy_reads(measure_count):
    return HEADER_READS + (measure_count * FIELDS_PER_MEASURE)


def packed_reads(measure_count):
    return HEADER_READS + (1 if measure_count > 0 else 0)


def percent_reduction(before, after):
    if before == 0:
        return 0.0
    return ((before - after) / before) * 100.0


def main():
    parser = argparse.ArgumentParser(
        description="Estimate PLC reads before/after packed MoveOutAndTestResults measure data."
    )
    parser.add_argument(
        "counts",
        nargs="*",
        type=int,
        default=[0, 9, 29, 32, 45, 49, 60, 100],
        help="Measurement counts to estimate. Defaults cover common production ranges.",
    )
    args = parser.parse_args()

    print("MeasureData rows | Legacy PLC reads | Packed PLC reads | Saved reads | Reduction")
    print("-----------------|------------------|------------------|-------------|----------")
    for count in args.counts:
        before = legacy_reads(count)
        after = packed_reads(count)
        saved = before - after
        reduction = percent_reduction(before, after)
        print(f"{count:16d} | {before:16d} | {after:16d} | {saved:11d} | {reduction:8.1f}%")


if __name__ == "__main__":
    main()
