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

    // TODO: Add messages for requesting to host and when someone joins
    typedef enum
    {
        START_SHARING,
        STOP_SHARING,
        HOST_CODE,
        JOIN_CODE,
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