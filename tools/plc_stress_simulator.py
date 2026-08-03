#!/usr/bin/env python3
"""Offline stress simulator for Zimple PLC/MES trigger execution.

This does not talk to a real PLC or MES. It models the current application
contract: one execution queue per PLC, one global gate for trigger 8, and the
existing PLC tag structure for Serial_MoveOutAndTestResults.
"""

import argparse
import asyncio
import random
import statistics
import time
from dataclasses import dataclass


MOVEOUT_AND_TEST_RESULTS = 8

TRIGGER_COSTS = {
    1: (3, 2),
    2: (1, 2),
    3: (3, 2),
    4: (4, 2),
    5: (4, 2),
    6: (5, 2),
    7: (2, 2),
    9: (3, 2),
    10: (2, 2),
    11: (4, 2),
    12: (2, 2),
    13: (2, 2),
    14: (3, 2),
    15: (2, 2),
    16: (2, 2),
}


@dataclass
class Request:
    plc: int
    op: int
    trigger: int
    measures: int
    queued_at: float


@dataclass
class Result:
    plc: int
    trigger: int
    reads: int
    writes: int
    elapsed_ms: float
    queued_ms: float


class StressModel:
    def __init__(self, args):
        self.args = args
        self.heavy_gate = None
        self.results = []
        self.heavy_in_flight = 0
        self.max_heavy_in_flight = 0

    def moveout_reads(self, measures):
        return 8 + (9 * measures)

    async def plc_io(self, reads, writes):
        delay_ms = (reads * self.args.read_ms) + (writes * self.args.write_ms)
        await asyncio.sleep((delay_ms / self.args.speedup) / 1000.0)

    async def mes_call(self):
        delay = (random.uniform(self.args.mes_min_ms, self.args.mes_max_ms) / self.args.speedup) / 1000.0
        await asyncio.sleep(delay)

    async def execute_heavy_body(self, request, reads, writes):
        self.heavy_in_flight += 1
        self.max_heavy_in_flight = max(self.max_heavy_in_flight, self.heavy_in_flight)
        try:
            await self.plc_io(reads, 0)
            pause_count = max(0, (request.measures - 1) // 5)
            await asyncio.sleep(((pause_count * self.args.pause_ms) / self.args.speedup) / 1000.0)
            await self.mes_call()
            await self.plc_io(0, writes)
        finally:
            self.heavy_in_flight -= 1

    async def execute(self, request):
        started = time.perf_counter()
        if request.trigger == MOVEOUT_AND_TEST_RESULTS:
            reads = self.moveout_reads(request.measures)
            writes = 2
            if self.args.disable_heavy_gate:
                await self.execute_heavy_body(request, reads, writes)
            else:
                async with self.heavy_gate:
                    await self.execute_heavy_body(request, reads, writes)
        else:
            reads, writes = TRIGGER_COSTS[request.trigger]
            await self.plc_io(reads, 0)
            await self.mes_call()
            await self.plc_io(0, writes)

        ended = time.perf_counter()
        self.results.append(
            Result(
                plc=request.plc,
                trigger=request.trigger,
                reads=reads,
                writes=writes,
                elapsed_ms=(ended - started) * 1000.0 * self.args.speedup,
                queued_ms=(started - request.queued_at) * 1000.0 * self.args.speedup,
            )
        )

    async def plc_worker(self, plc, requests):
        for request in requests:
            await self.execute(request)

    async def run(self):
        self.heavy_gate = asyncio.Semaphore(1)
        per_plc = [[] for _ in range(self.args.plcs)]
        triggers = sorted(TRIGGER_COSTS.keys())
        burst_time = time.perf_counter()
        for plc in range(self.args.plcs):
            for index in range(self.args.ops_per_plc):
                is_heavy = random.random() < self.args.moveout_ratio
                trigger = MOVEOUT_AND_TEST_RESULTS if is_heavy else random.choice(triggers)
                measures = random.randint(self.args.min_measures, self.args.max_measures)
                per_plc[plc].append(
                    Request(plc=plc, op=index, trigger=trigger, measures=measures, queued_at=burst_time)
                )

        start = time.perf_counter()
        await asyncio.gather(*(self.plc_worker(plc, reqs) for plc, reqs in enumerate(per_plc)))
        return (time.perf_counter() - start) * 1000.0 * self.args.speedup


def percentile(values, pct):
    if not values:
        return 0.0
    ordered = sorted(values)
    index = int(round((pct / 100.0) * (len(ordered) - 1)))
    return ordered[index]


def print_summary(results, elapsed_ms, max_heavy_in_flight):
    reads = sum(item.reads for item in results)
    writes = sum(item.writes for item in results)
    heavy = [item for item in results if item.trigger == MOVEOUT_AND_TEST_RESULTS]
    light = [item for item in results if item.trigger != MOVEOUT_AND_TEST_RESULTS]
    elapsed = [item.elapsed_ms for item in results]

    print("Stress simulation completed")
    print(f"Requests: {len(results)} total, {len(heavy)} MoveOutAndTestResults, {len(light)} other")
    print(f"PLC operations: {reads} reads, {writes} writes, {reads + writes} total")
    print(f"Modeled wall time: {elapsed_ms:.0f} ms")
    print(f"Max simultaneous MoveOutAndTestResults: {max_heavy_in_flight}")
    print(
        "Request elapsed ms: "
        f"avg={statistics.mean(elapsed):.0f}, "
        f"p50={percentile(elapsed, 50):.0f}, "
        f"p95={percentile(elapsed, 95):.0f}, "
        f"max={max(elapsed):.0f}"
    )

    if heavy:
        heavy_reads = sum(item.reads for item in heavy)
        heavy_elapsed = [item.elapsed_ms for item in heavy]
        print(
            "MoveOutAndTestResults: "
            f"reads={heavy_reads}, "
            f"avg_ms={statistics.mean(heavy_elapsed):.0f}, "
            f"p95_ms={percentile(heavy_elapsed, 95):.0f}, "
            f"max_ms={max(heavy_elapsed):.0f}"
        )


def main():
    parser = argparse.ArgumentParser(description="Stress-test Zimple's current PLC request pattern offline.")
    parser.add_argument("--plcs", type=int, default=2)
    parser.add_argument("--ops-per-plc", type=int, default=40)
    parser.add_argument("--moveout-ratio", type=float, default=0.65)
    parser.add_argument("--min-measures", type=int, default=45)
    parser.add_argument("--max-measures", type=int, default=60)
    parser.add_argument("--read-ms", type=float, default=8.0)
    parser.add_argument("--write-ms", type=float, default=8.0)
    parser.add_argument("--mes-min-ms", type=float, default=80.0)
    parser.add_argument("--mes-max-ms", type=float, default=250.0)
    parser.add_argument("--pause-ms", type=float, default=50.0)
    parser.add_argument("--speedup", type=float, default=1.0)
    parser.add_argument("--disable-heavy-gate", action="store_true")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    random.seed(args.seed)
    model = StressModel(args)
    elapsed_ms = asyncio.run(model.run())
    print_summary(model.results, elapsed_ms, model.max_heavy_in_flight)


if __name__ == "__main__":
    main()
