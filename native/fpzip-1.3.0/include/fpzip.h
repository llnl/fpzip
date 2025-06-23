#pragma once
// Dummy fpzip.h for placeholder C++ build
// Define minimal things that might be expected by other dummy files or CMake setup

#define FPZIP_FP_SAFE 2
// Assume FPZIP_FP is set by compiler definition from CMakeLists.txt

typedef struct {
  int type;
  int prec;
  int nx;
  int ny;
  int nz;
  int nf;
} FPZ;

// Dummy declarations for functions that might be called by utils/fpzip.cpp
#ifdef __cplusplus
extern "C" {
#endif

FPZ* fpzip_write_to_file(void* file);
int fpzip_write_header(FPZ* fpz);
// size_t fpzip_write(FPZ* fpz, const void* data); // size_t might need <cstddef>
void fpzip_write_close(FPZ* fpz);

FPZ* fpzip_read_from_file(void* file);
int fpzip_read_header(FPZ* fpz);
// size_t fpzip_read(FPZ* fpz, void* data);
void fpzip_read_close(FPZ* fpz);

#ifdef __cplusplus
}
#endif
