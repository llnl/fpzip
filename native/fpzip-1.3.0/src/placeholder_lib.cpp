// Dummy .cpp file for the fpzip_lib target in placeholder CMakeLists.txt
// In a real build, this would be multiple .cpp files like read.cpp, write.cpp, etc.
// This single file ensures the 'fpzip_lib' target has a source.
#include "fpzip.h" // From include directory

// Dummy function to ensure some code exists for the library
void fpzip_dummy_lib_function() {
    // This function would not exist in real fpzip
    // It's just to make the placeholder library compile.
}

// Provide minimal implementations for functions linked by dummy utils/fpzip.cpp
// This is highly simplified and not functional fpzip code.
#ifdef __cplusplus
extern "C" {
#endif

FPZ* fpzip_write_to_file(void* file) { return (FPZ*)0L; }
int fpzip_write_header(FPZ* fpz) { return 0; }
// size_t fpzip_write(FPZ* fpz, const void* data) { return 0; }
void fpzip_write_close(FPZ* fpz) { }

FPZ* fpzip_read_from_file(void* file) { return (FPZ*)0L; }
int fpzip_read_header(FPZ* fpz) { return 0; }
// size_t fpzip_read(FPZ* fpz, void* data) { return 0; }
void fpzip_read_close(FPZ* fpz) { }

#ifdef __cplusplus
}
#endif
