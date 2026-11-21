"""Versioned AnimalTown profiling wire primitives (no emulator dependency)."""
MEMORY_NAMES = (
    'schema', 'validity', 'mem1_total_bytes', 'arena_free_bytes', 'malloc_free_bytes',
    'mem1_used_bytes', 'mem1_sampled_peak_bytes', 'managed_live_bytes',
    'managed_lifetime_peak_bytes', 'managed_capacity_bytes', 'collections',
    'draw_owned_bytes', 'draw_sampled_peak_bytes', 'model_allocator_live_bytes',
    'model_allocator_lifetime_peak_bytes', 'aram_total_bytes', 'aram_used_bytes',
    'aram_largest_free_bytes', 'aram_slots', 'terrain_draw_subset_bytes',
    'terrain_cache_live_bytes', 'terrain_cache_peak_bytes', 'terrain_largest_free_bytes',
    'terrain_aram_payload_bytes', 'terrain_sync_reads', 'terrain_sync_wait_us',
    'terrain_async_reads', 'terrain_sync_read_bytes', 'memory_query_us',
    'sample_clock_low32_us', 'unattributed_mem1_estimate_bytes', 'scope_flags')


def crc(words, seed=65535):
    for word in words:
        for byte in (word & 255, (word >> 8) & 255):
            seed ^= byte << 8
            for _ in range(8):
                seed = ((seed << 1) ^ (0x1021 if seed & 32768 else 0)) & 65535
    return seed


def u32(words, at):
    return words[at] | (words[at + 1] << 16)


def valid_identity(main, detail, submissions, memory, identity):
    return (identity[:2] == [0x4149, 1] and identity[4] == main[2]
            and detail[:2] == [0x4144, main[2]]
            and submissions[:3] == [0x4153, 1, main[2]]
            and memory[:2] == [0x414d, 1] and memory[2:4] == identity[2:4]
            and memory[4:6] == identity[9:11] and 0 <= memory[6] < 8
            and crc(memory[:15]) == memory[15]
            and crc(main + detail + submissions + memory + identity[:15]) == identity[15])


def memory_report(words):
    result = dict(zip(MEMORY_NAMES, words))
    result['scope'] = ('Physical/draw peaks are sampled. Managed/model peaks track allocations from boot. '
                       'Model requests include existing allocator headers/alignment and shared textures once. '
                       'Terrain is a subset of draw ownership; managed capacity includes managed live bytes. '
                       'Unattributed MEM1 includes executable/static data, stacks, audio, frame surfaces, '
                       'other allocators and overhead. MEM1 largest block is unknown. EFB is separate.')
    result['mem1_largest_free_bytes'] = None
    return result
