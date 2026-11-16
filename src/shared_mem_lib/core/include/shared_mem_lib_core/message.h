#ifndef MESSAGE_H

#define MESSAGE_H

#include <stdint.h>
#include "encoding.h"

#ifdef __cplusplus
extern "C"
{
#endif

    typedef enum
    {
        Processor,
        Network,
        Application,
    } Module;

    typedef enum
    {
        START_SHARING,
        STOP_SHARING,
        // SET_SHARE_PROCESS_ID,
        // SET_SHARE_SCREEN_ID,
    } Operation;

    typedef struct
    {
        uint8_t data[256];
        Module src, dest;
        Operation opcode;
    } Message;
#ifdef __cplusplus
}
#endif

#endif