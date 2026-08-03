#!/usr/bin/env python3
"""Estimate PLC read pressure for the current Serial_MoveOutAndTestResults contract."""

import argparse


HEADER_READS = 8
FIELDS_PER_MEASURE = 9


def legacy_reads(measure_count):
    return HEADER_READS + (measure_count * FIELDS_PER_MEASURE)


def main():
    parser = argparse.ArgumentParser(
        description="Estimate PLC reads for MoveOutAndTestResults using the current PLC tag structure."
    )
    parser.add_argument(
        "counts",
        nargs="*",
        type=int,
        default=[0, 9, 29, 32, 45, 49, 60, 100],
        help="Measurement counts to estimate. Defaults cover common production ranges.",
    )
    args = parser.parse_args()

    print("MeasureData rows | Current PLC reads | PLC writes | Total PLC ops")
    print("-----------------|-------------------|------------|--------------")
    for count in args.counts:
        reads = legacy_reads(count)
        writes = 2
        print(f"{count:16d} | {reads:17d} | {writes:10d} | {reads + writes:12d}")


if __name__ == "__main__":
    main()
