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
        HOST_CODE,
        ANSWER_CODE,
        START_HOST,
        JOIN_CALL,
        PARTICIPANT_JOINED,
        PARTICIPANT_LEFT,
        LEAVE_CALL,
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