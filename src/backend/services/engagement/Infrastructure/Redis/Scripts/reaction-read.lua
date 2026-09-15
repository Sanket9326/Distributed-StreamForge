#!lua flags=no-writes
-- @common
expect(KEYS[1], 'set'); expect(KEYS[2], 'set')
if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 1 then return 'like' end
if redis.call('SISMEMBER', KEYS[2], ARGV[1]) == 1 then return 'dislike' end
return 'none'
