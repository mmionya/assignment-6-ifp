#ifndef ASTRA_NATIVE_H
#define ASTRA_NATIVE_H
#if defined(_WIN32)
#define ASTRA_API __declspec(dllexport)
#else
#define ASTRA_API __attribute__((visibility("default")))
#endif
ASTRA_API int astra_group_eligible(int passengers, int minimum);
#endif
