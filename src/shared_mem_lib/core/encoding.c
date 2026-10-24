#include "encoding.h"
#include "openssl/rand.h"

uint64_t generate_id()
{
    uint64_t out;
    // This should be fine for unique ids, collisions are unlikely
    if (RAND_bytes(&out, sizeof(uint64_t)) != 1)
    {
        printf("Failed to generate id, returning 0");
        return 0;
    }

    return out;
}

Encoding new_encoding()
{
    Encoding enc;
    enc.id = generate_id();
    return enc;
}

Encoding nil_encoding()
{
    Encoding enc;
    enc.id = 0;
    return enc;
}

bool enc_eq(Encoding a, Encoding b)
{
    return a.id == b.id;
}