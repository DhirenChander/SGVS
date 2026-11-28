#include <stdint.h>
#include "shared_mem_lib_core/framebuff.h"
#include "shared_mem_lib_core/ring.h"
#include "shared_mem_lib_core/ringbuff.h"
#include <stdio.h>
#include <string.h>

int ringbuff_by_encoding(FrameBuffer *fb, Encoding enc);
static uint256_t message_channel_id(Module src, Module dst);

static uint256_t message_channel_id(Module src, Module dst)
{
    uint256_t out = uint256_zero();
    out.limbs[0] = 0x534756535F4D5347ULL; // "SGVS_MSG"
    out.limbs[1] = ((uint64_t)(uint32_t)src << 32) | (uint32_t)dst;
    out.limbs[2] = 0x4348414E4E454C31ULL; // "CHANNEL1"
    out.limbs[3] = 1;
    return out;
}

bool config_has_encoding(Config *conf, Encoding enc)
{
    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(conf->encodings[i], enc))
        {
            return true;
        }
    }

    return false;
}

// Opens the ring first, without the lock (mapping a video ring takes a while), then appends it under the lock.
// Several threads can add the same encoding at once (each one aligning to the config): the first to append wins
// and the others free their copy, so every ring is in the list once and every entry in it is fully opened.
bool add_ring(FrameBuffer *fb, Encoding enc)
{
    RingBuffer rb;
    memset(&rb, 0, sizeof(rb));
    rb.id = enc.id;
    // TODO: Make this dependent on the encoding
    if (!open_ring(&rb, (enc.type == Audio) ? 1275 : 500000, (enc.type == Audio) ? 201 : 121))
        return false;

    bool present = false;
    bool appended = false;

#ifdef _WIN32
    // TODO: Change this to SRW Exclusive
    EnterCriticalSection(&fb->cs);
#endif

    for (int i = 0; i < fb->ring_count && !present; i++)
        present = uint256_eq(fb->rings[i].id, enc.id);

    if (!present)
    {
        // TODO: Use a more efficient allocating strategy when allocating rings
        // realloc into a temporary: on failure the old list is still valid
        RingBuffer *grown = (RingBuffer *)realloc(fb->rings, (fb->ring_count + 1) * sizeof(RingBuffer));
        if (grown != nullptr)
        {
            fb->rings = grown;
            fb->rings[fb->ring_count] = rb;
            fb->ring_count += 1;
            appended = true;
        }
    }

#ifdef _WIN32
    LeaveCriticalSection(&fb->cs);
#endif

    if (appended)
        return true;

    free_ringbuffer(&rb);
    if (present)
        return true;

    char id_str[80];
    write_uint256(id_str, enc.id);
    printf("Unable to allocate new ring for encoding: %s\n", id_str);
    return false;
}

bool remove_ring(FrameBuffer *fb, Encoding enc)
{
    int rb_ind = ringbuff_by_encoding(fb, enc);

    if (rb_ind == -1)
    {
        return true;
    }

    free_ringbuffer(&fb->rings[rb_ind]);

#ifdef _WIN32
    // TODO: Change this to SRW Exclusive
    EnterCriticalSection(&fb->cs);
#endif

    memmove(&fb->rings[rb_ind], &fb->rings[rb_ind + 1], (fb->ring_count - rb_ind - 1) * sizeof(RingBuffer));
    fb->ring_count -= 1;
    fb->rings = (RingBuffer *)realloc(fb->rings, fb->ring_count * sizeof(RingBuffer));

#ifdef _WIN32
    LeaveCriticalSection(&fb->cs);
#endif

    if (fb->rings == nullptr)
    {
        char id_str[80];
        write_uint256(id_str, enc.id);
        printf("Unable to allocate remove ring for encoding: %s\n", id_str);
        return false;
    }

    return true;
}

bool request_encoding(FrameBuffer *fb, Encoding enc)
{
    if (config_has_encoding(fb->config, enc))
    {
        return true;
    }

    Encoding nil = nil_encoding();

#ifdef _WIN32
    if (WaitForSingleObject(fb->conf_mu, INFINITE) != WAIT_OBJECT_0)
    {
        return false;
    }
#endif

    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(fb->config->encodings[i], nil))
        {
            fb->config->encodings[i] = enc;

#ifdef _WIN32
            ReleaseMutex(fb->conf_mu);
#endif

            if (!add_ring(fb, enc))
            {
                // Clean up if ring creation fails
                fb->config->encodings[i] = nil;
                return false;
            }

#ifdef _WIN32
            SetEvent(fb->encs_ev); // Only set the event on suc
#endif

            return true;
        }
    }

#ifdef _WIN32
    ReleaseMutex(fb->conf_mu);
#endif

    return false;
}

bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len)
{
    (*out_len) = 0;
    Encoding nil = nil_encoding();

    for (int i = 0; i < MAX_ENCODINGS && *out_len < max_len; i++)
    {
        if (!enc_eq(fb->config->encodings[i], nil))
        {
            out[*out_len] = fb->config->encodings[i];
            (*out_len)++;
        }
    }

    return true;
}

bool blocking_read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout)
{
#ifdef _WIN32
    DWORD waitResult = WaitForSingleObject(fb->encs_ev, (DWORD)max_timeout);

    switch (waitResult)
    {
    case WAIT_OBJECT_0:
        break;

    case WAIT_TIMEOUT:
        printf("No updates within the timeout, returning.\n");
        break;

    case WAIT_FAILED:
        printf("WaitForSingleObject failed with error (%lu)\n", GetLastError());
        break;

    default:
        printf("Unexpected wait result: %lu\n", waitResult);
        break;
    }

    if (waitResult != WAIT_OBJECT_0)
    {
        return false;
    }
#endif

    return read_encodings(fb, out, out_len, max_len);
}

bool remove_encoding(FrameBuffer *fb, Encoding enc)
{
#ifdef _WIN32
    if (WaitForSingleObject(fb->conf_mu, INFINITE) != WAIT_OBJECT_0)
    {
        return false;
    }
#endif

    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (enc_eq(fb->config->encodings[i], enc))
        {
            fb->config->encodings[i] = nil_encoding();

#ifdef _WIN32
            ReleaseMutex(fb->conf_mu);
#endif

            if (!remove_ring(fb, enc))
            {
                // Clean-up if ring removal fails
                fb->config->encodings[i] = enc;
                return false;
            }

#ifdef _WIN32
            SetEvent(fb->encs_ev);
#endif

            return true;
        }
    }

#ifdef _WIN32
    ReleaseMutex(fb->conf_mu);
#endif

    return true;
}

int ringbuff_by_encoding(FrameBuffer *fb, Encoding enc)
{
    uint256_t id = enc.id;
    int ind = -1;

#ifdef _WIN32
    EnterCriticalSection(&fb->cs);
#endif

    for (int i = 0; i < fb->ring_count; i++)
    {
        if (uint256_eq(fb->rings[i].id, id))
        {
            ind = i;
            break;
        }
    }

#ifdef _WIN32
    LeaveCriticalSection(&fb->cs);
#endif

    return ind;
}

// Copies the ring's handle under the lock so a blocking pop or push can run without holding it.
// The copy only holds pointers and handles into the shared mapping, and nothing removes rings while frames move.
// add_ring only appends rings that are already open, so a ring found here is always usable.
static bool copy_ring_by_encoding(FrameBuffer *fb, Encoding enc, RingBuffer *out)
{
    bool found = false;

#ifdef _WIN32
    EnterCriticalSection(&fb->cs);
#endif

    for (int i = 0; i < fb->ring_count; i++)
    {
        if (uint256_eq(fb->rings[i].id, enc.id))
        {
            *out = fb->rings[i];
            found = true;
            break;
        }
    }

#ifdef _WIN32
    LeaveCriticalSection(&fb->cs);
#endif

    return found;
}

bool try_pop_frames(FrameBuffer *fb, Encoding enc, Frame *out, uint32_t *out_count, uint32_t max_frame_count)
{
    return blocking_pop_frames(fb, enc, out, out_count, max_frame_count, 0x0);
}

bool blocking_pop_frames(FrameBuffer *fb, Encoding enc, Frame *out, uint32_t *out_count, uint32_t max_frame_count, uint32_t max_timeout)
{
    RingBuffer rb;

    if (!copy_ring_by_encoding(fb, enc, &rb))
    {
        char id_str[80];
        write_uint256(id_str, enc.id);
        printf("No ringbuffer present for this encoding: %s\n", id_str);
        return false;
    }

    Slot *raw_out = (Slot *)malloc(max_frame_count * sizeof(Slot));

    for (uint32_t i = 0; i < max_frame_count; i++)
    {
        // Bytes is a pointer, so by copying to the raw slot, the data auto copies to the outframes.
        raw_out[i].bytes = out[i].data;
        raw_out[i].len = out[i].size;
    }

    // The lock is not held here: a long wait on one ring must not stall pushes and pops on the others
    bool res = blocking_pop(&rb, raw_out, out_count, max_frame_count, max_timeout);

    if (!res)
    {
        free(raw_out);
        return false;
    }

    for (uint32_t i = 0; i < *out_count; i++)
    {
        out[i].size = raw_out[i].len;
        out[i].encoding = enc;
    }

    free(raw_out);

    return true;
}

bool push_frames(FrameBuffer *fb, Frame *f, uint32_t frame_count)
{
    RingBuffer rb;

    if (!copy_ring_by_encoding(fb, f->encoding, &rb))
    {
        char id_str[80];
        write_uint256(id_str, f->encoding.id);
        printf("No ring buffer present for this encoding: %s\n", id_str);
        return false;
    }

    Slot *data = (Slot *)malloc(frame_count * sizeof(Slot));

    for (uint32_t i = 0; i < frame_count; i++)
    {
        data[i].bytes = f[i].data;
        data[i].len = f[i].size;
    }

    bool res = push(&rb, data, frame_count);

    free(data);

    return res;
}

bool new_framebuffer(FrameBuffer *out)
{
    // TODO: Use a more efficient allocating strategy when allocating rings
    out->rings = malloc(0);
    out->ring_count = 0;

    uint64_t conf_size = sizeof(Config);
    Config *config;

    for (int i = 0; i < MSG_EV_COUNT; i++)
    {
        for (int j = 0; j < MSG_EV_COUNT; j++)
        {
            out->msg_chans[i][j].id = message_channel_id((Module)i, (Module)j);

            if (!open_ring(&out->msg_chans[i][j], sizeof(Message), 10))
            {
                return false;
            }
        }
    }

#ifdef _WIN32
    // every named object gets the SGVS_SHM_INSTANCE suffix, so two instances can share a PC
    wchar_t suffix[18];
    shm_name_suffix(suffix);
    wchar_t mu_name[64], conf_name[64], ev_name[64];
    swprintf(mu_name, 64, L"Local\\svgs_mu%ls", suffix);
    swprintf(conf_name, 64, L"Local\\sgvs_conf%ls", suffix);
    swprintf(ev_name, 64, L"Local\\sgvs_conf_encs_ev%ls", suffix);

    // Create mutex that is immediately owned
    out->conf_mu = CreateMutexW(NULL, TRUE, mu_name);

    if (out->conf_mu == nullptr)
    {
        return false;
    }

    if (GetLastError() == ERROR_ALREADY_EXISTS)
    {
        // Must wait forever until the other processes are done editing the frame buffer
        WaitForSingleObject(out->conf_mu, INFINITE);
    }

    out->conf_map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                       (DWORD)(conf_size >> 32), (DWORD)(conf_size & 0xFFFFFFFF),
                                       conf_name);
    if (out->conf_map == nullptr)
    {
        free_framebuffer(out);
        return false;
    }

    DWORD err = GetLastError();

    config = (Config *)MapViewOfFile(out->conf_map, FILE_MAP_ALL_ACCESS, 0, 0, conf_size);

    if (config == nullptr)
    {
        free_framebuffer(out);
        return false;
    }

    if (err == 0)
    {
        memset(config, 0, conf_size);
    }

    // TODO: Change this to an event that is suitable to multiple waiters
    out->encs_ev = CreateEventW(nullptr, FALSE, FALSE, ev_name);
    if (out->encs_ev == nullptr)
    {
        free_framebuffer(out);
        return false;
    }

    if (out->conf_mu == nullptr)
    {
        free_framebuffer(out);
        return false;
    }

    InitializeCriticalSection(&out->cs);
    ReleaseMutex(out->conf_mu);
#endif

    out->config = config;

    if (!align_rings_to_config(out))
    {
        return false;
    }

    return true;
}

void free_framebuffer(FrameBuffer *fb)
{
    for (int i = 0; i < fb->ring_count; i++)
    {
        free_ringbuffer(&fb->rings[i]);
    }
    free(fb->rings);
#ifdef _WIN32
    UnmapViewOfFile(fb->config);
    CloseHandle(fb->conf_map);
    CloseHandle(fb->encs_ev);
    CloseHandle(fb->conf_mu);
    DeleteCriticalSection(&fb->cs);
#endif
}

bool align_rings_to_config(FrameBuffer *fb)
{
    Encoding nil = nil_encoding();

    for (int i = 0; i < MAX_ENCODINGS; i++)
    {
        if (!enc_eq(fb->config->encodings[i], nil))
        {
            int ind = ringbuff_by_encoding(fb, fb->config->encodings[i]);

            if (ind == -1)
            {
                if (!add_ring(fb, fb->config->encodings[i]))
                    return false;
            }
        }
    }

    return true;
    // TODO: Add code to delete old rings
}

bool push_message(FrameBuffer *fb, Message *msg)
{
    Slot slot;
    slot.bytes = (uint8_t *)calloc(sizeof(Message), 1);
    slot.len = sizeof(Message);

    memcpy(slot.bytes, msg, sizeof(Message));

    bool res = push(&fb->msg_chans[msg->src][msg->dest], &slot, 1);

    free(slot.bytes);

    return res;
}

bool blocking_pop_message(FrameBuffer *fb, Module src, Module dst, Message *out_msg, uint32_t max_timeout)
{
    Slot out;
    out.bytes = (uint8_t *)malloc(sizeof(Message));
    out.len = sizeof(Message);

    uint32_t _;

    if (!blocking_pop(&fb->msg_chans[src][dst], &out, &_, 1, max_timeout))
    {
        free(out.bytes);
        return false;
    }

    memcpy(out_msg, out.bytes, sizeof(Message));

    free(out.bytes);

    return true;
}
