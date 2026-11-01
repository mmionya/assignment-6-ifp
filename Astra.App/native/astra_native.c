#include "astra_native.h"

int astra_group_eligible(int passengers, int minimum)
{
    if (passengers < 1 || passengers > 6 || minimum < 1 || minimum > 6)
        return -1;
    return passengers >= minimum ? 1 : 0;
}
