#!/usr/bin/env python3
"""Estimate PLC read pressure for Serial_MoveOutAndTestResults contracts."""

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
        description="Estimate PLC reads for MoveOutAndTestResults using legacy and packed contracts."
    )
    parser.add_argument(
        "counts",
        nargs="*",
        type=int,
        default=[0, 9, 29, 32, 45, 49, 60, 100],
        help="Measurement counts to estimate. Defaults cover common production ranges.",
    )
    args = parser.parse_args()

    print("MeasureData rows | Legacy reads | Packed reads | Saved reads | Reduction | Packed total ops")
    print("-----------------|--------------|--------------|-------------|-----------|-----------------")
    for count in args.counts:
        before = legacy_reads(count)
        after = packed_reads(count)
        saved = before - after
        writes = 2
        reduction = percent_reduction(before, after)
        print(f"{count:16d} | {before:12d} | {after:12d} | {saved:11d} | {reduction:9.1f}% | {after + writes:15d}")


if __name__ == "__main__":
    main()
